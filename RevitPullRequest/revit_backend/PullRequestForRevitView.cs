using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using PullRequestForRevit.Models;
using PullRequestForRevit.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Newtonsoft.Json;
using Nice3point.Revit.Extensions;

namespace PullRequestForRevit;

    public class PullRequestForRevitView : UserControl
    {
        public static PullRequestForRevitView? Instance { get; private set; }

        private WebView2? _webView;
        private const string FrontendUrl = "http://localhost:8001";
        private readonly string _dumpFolder;
        private readonly string _parquetFolder;
        private readonly string _sessionId;
        
        // Configuration for session cleanup
        private const int MaxSessionAgeDays = 7; // Keep sessions for 7 days
        private const int MaxSessionsToKeep = 10; // Keep at most 10 most recent sessions

        public PullRequestForRevitView()
        {
            var baseDumpFolder = Path.Combine(Directory.GetCurrentDirectory(), "DUMP");
            
            // Create a unique session ID: timestamp + process ID + GUID for maximum uniqueness
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var guid = Guid.NewGuid().ToString("N")[..8];
            _sessionId = $"{timestamp}_{processId}_{guid}";
            
            // Create session-specific folder: DUMP/session_<timestamp>_<pid>_<guid>
            _dumpFolder = Path.Combine(baseDumpFolder, $"session_{_sessionId}");
            
            // Clean up old session folders before creating new one
            CleanupOldSessionFolders(baseDumpFolder);
            
            // Ensure session folder exists
            if (!Directory.Exists(_dumpFolder))
            {
                Directory.CreateDirectory(_dumpFolder);
                Logger.Instance.LogInfo($"Created session folder: {_dumpFolder}");
            }
            
            // Initialize logger with session ID for session-specific logging
            Logger.Instance.InitializeSession(_sessionId);
            
            Logger.Instance.LogInfo($"Revit session initialized with ID: {_sessionId}");
            Logger.Instance.LogInfo($"Session dump folder: {_dumpFolder}");
            
            _parquetFolder = Path.Combine(Directory.GetCurrentDirectory(), "flat.parquet");
        
            var grid = new System.Windows.Controls.Grid();

            _webView = new WebView2()
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            grid.Children.Add(_webView);
            Content = grid;

            Dispatcher.InvokeAsync(InitializeWebViewAsync);
            AttachEventHandlers();

            Instance = this;
        }

    private void AttachEventHandlers()
    {
        // Host event - selection changed
        Application.ActionEventHandler.Raise(app =>
        {
            app.SelectionChanged += OnSelectionChanged;
        });

        // Web event - incoming messages
        if (_webView != null)
        {
            _webView.WebMessageReceived += OnWebMessageReceived;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            Logger.Instance.LogDebug($"Received message from frontend: {e.WebMessageAsJson}");

            // First try to deserialize as a generic WebMessage to get the type
            var baseMessage = JsonConvert.DeserializeObject<WebMessage>(e.WebMessageAsJson);
            if (baseMessage == null || string.IsNullOrEmpty(baseMessage.Type))
            {
                Logger.Instance.LogWarning("Received invalid message from frontend");
                return;
            }

            Application.ActionEventHandler.Raise(app =>
            {
                try
                {
                    if (app?.ActiveUIDocument?.Document == null)
                    {
                        Logger.Instance.LogWarning("Cannot process message: No active document");
                        return;
                    }

                    // Deserialize to specific message type based on the type field
                    switch (baseMessage.Type)
                    {
                        case "record:elements":
                            var recordMessage = JsonConvert.DeserializeObject<RecordMessage>(e.WebMessageAsJson);
                            if (recordMessage != null)
                            {
                                HandleRecordElements(app, recordMessage);
                            }
                            break;
                        case "compare:elements":
                            var compareMessage = JsonConvert.DeserializeObject<CompareMessage>(e.WebMessageAsJson);
                            if (compareMessage != null)
                            {
                                HandleCompareElements(app, compareMessage);
                            }
                            break;
                        case "compare:confirmAll":
                            Logger.Instance.LogInfo("Received compare:confirmAll message - allowing sync");
                            SyncGuard.ConfirmAll();
                            break;
                        case "compare:submitForReview":
                            try
                            {
                                Logger.Instance.LogInfo("Received compare:submitForReview message - sync request sent to PM (stub)");

                                var doc = app.ActiveUIDocument.Document;
                                if (doc.IsReadOnly)
                                {
                                    Logger.Instance.LogWarning("Cannot save document on submit for review: document is read-only.");
                                }
                                else
                                {
                                    if (!doc.IsModified)
                                    {
                                        Logger.Instance.LogInfo("Document not modified, skipping save on submit for review.");
                                    }
                                    else
                                    {
                                        doc.Save();
                                        Logger.Instance.LogInfo("Document saved locally on submit for review.");
                                        SendNotification("Revit file saved locally on submit for review.", "success");
                                    }
                                }
                            }
                            catch (Exception exSubmit)
                            {
                                Logger.Instance.LogError("Error saving document on submit for review", exSubmit);
                            }
                            break;
                        case "compare:rejectChanges":
                            Logger.Instance.LogInfo("Received compare:rejectChanges message - changes rejected by PM. Sync remains blocked.");
                            SyncGuard.RequireConfirmation();
                            break;
                        case "clear:visualization":
                            HandleClearVisualization(app);
                            break;
                        case "get:selection":
                            HandleGetSelection(app);
                            break;
                        case "parquet:get:files":
                            try
                            {
                                HandleGetParquetFiles();
                            }
                            catch (Exception parquetEx)
                            {
                                Logger.Instance.LogError("Unhandled error in parquet:get:files", parquetEx);
                                var errorResponse = new ParquetFilesResponse
                                {
                                    Files = new List<string>(),
                                    Error = "Failed to get parquet files list"
                                };
                                SendMessage(errorResponse);
                            }
                            break;
                        case "parquet:get:info":
                            try
                            {
                                var infoMessage = JsonConvert.DeserializeObject<GetParquetFileInfoMessage>(e.WebMessageAsJson);
                                if (infoMessage != null)
                                {
                                    _ = HandleGetParquetFileInfo(infoMessage).ContinueWith(task =>
                                    {
                                        if (task.IsFaulted)
                                        {
                                            Logger.Instance.LogError("Unhandled error in parquet:get:info", task.Exception);
                                        }
                                    }, TaskContinuationOptions.OnlyOnFaulted);
                                }
                                else
                                {
                                    var errorResponse = new ParquetFileInfoResponse
                                    {
                                        Error = "Invalid message format"
                                    };
                                    SendMessage(errorResponse);
                                }
                            }
                            catch (Exception parquetEx)
                            {
                                Logger.Instance.LogError("Unhandled error in parquet:get:info", parquetEx);
                                var errorResponse = new ParquetFileInfoResponse
                                {
                                    Error = "Failed to get parquet file info"
                                };
                                SendMessage(errorResponse);
                            }
                            break;
                        case "parquet:get:data":
                            try
                            {
                                var dataMessage = JsonConvert.DeserializeObject<GetParquetDataMessage>(e.WebMessageAsJson);
                                if (dataMessage != null)
                                {
                                    _ = HandleGetParquetData(dataMessage).ContinueWith(task =>
                                    {
                                        if (task.IsFaulted)
                                        {
                                            Logger.Instance.LogError("Unhandled error in parquet:get:data", task.Exception);
                                        }
                                    }, TaskContinuationOptions.OnlyOnFaulted);
                                }
                                else
                                {
                                    var errorResponse = new ParquetDataResponse
                                    {
                                        Rows = new List<Dictionary<string, object?>>(),
                                        Columns = new List<string>(),
                                        TotalRows = 0,
                                        Offset = 0,
                                        Limit = 100,
                                        Error = "Invalid message format"
                                    };
                                    SendMessage(errorResponse);
                                }
                            }
                            catch (Exception parquetEx)
                            {
                                Logger.Instance.LogError("Unhandled error in parquet:get:data", parquetEx);
                                var errorResponse = new ParquetDataResponse
                                {
                                    Rows = new List<Dictionary<string, object?>>(),
                                    Columns = new List<string>(),
                                    TotalRows = 0,
                                    Offset = 0,
                                    Limit = 100,
                                    Error = "Failed to read parquet data"
                                };
                                SendMessage(errorResponse);
                            }
                            break;
                        default:
                            Logger.Instance.LogWarning($"Unknown message type: {baseMessage.Type}");
                            break;
                    }
                }
                catch (Exception innerEx)
                {
                    Logger.Instance.LogError($"Error processing message type {baseMessage.Type}", innerEx);
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error handling web message", ex);
        }
    }

    private void HandleRecordElements(UIApplication app, RecordMessage message)
    {
        try
        {
            Logger.Instance.LogInfo("Handling record:elements message");

            var doc = app.ActiveUIDocument.Document;
            var selection = app.ActiveUIDocument.Selection;
            var selectedIds = selection.GetElementIds();

            if (selectedIds.Count == 0)
            {
                var response = new RecordCompleteMessage
                {
                    Success = false,
                    Error = "No elements selected"
                };
                SendMessage(response);
                Logger.Instance.LogWarning("Record requested but no elements selected");
                return;
            }

            var results = new List<RecordResult>();

            foreach (ElementId id in selectedIds)
            {
                try
                {
                    var element = doc.GetElement(id);
                    if (element == null)
                    {
                        results.Add(new RecordResult
                        {
                            UniqueId = id.ToString(),
                            Success = false,
                            Error = "Element not found"
                        });
                        continue;
                    }

                    var uniqueId = element.UniqueId;
                    var displayName = GetElementDisplayName(element);
                    Logger.Instance.LogInfo($"Recording element - UniqueId: {uniqueId}, ElementId: {id}, Name: {displayName}");

                    var data = ElementRecorder.RecordElement(element, doc);
                    if (data != null)
                    {
                        var saved = ElementRecorder.SaveToFile(data, _dumpFolder);
                        results.Add(new RecordResult
                        {
                            UniqueId = element.UniqueId,
                            Success = saved
                        });
                        Logger.Instance.LogInfo($"Recorded element: {element.UniqueId}");
                    }
                    else
                    {
                        results.Add(new RecordResult
                        {
                            UniqueId = element.UniqueId,
                            Success = false,
                            Error = "Failed to extract element data"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogError($"Error recording element {id}", ex);
                    results.Add(new RecordResult
                    {
                        UniqueId = id.ToString(),
                        Success = false,
                        Error = ex.Message
                    });
                }
            }

            var allSuccessful = results.All(r => r.Success);
            var completeMessage = new RecordCompleteMessage
            {
                Success = allSuccessful,
                Results = results
            };

            SendMessage(completeMessage);
            Logger.Instance.LogInfo($"Record complete: {results.Count(r => r.Success)}/{results.Count} successful");

            // After recording, require a comparison/confirmation before allowing sync.
            // This ensures that once a baseline is captured, users must run compare
            // (and resolve changes if any) before syncing to central.
            if (results.Count > 0 && allSuccessful)
            {
                SyncGuard.RequireConfirmation();
                SendNotification(
                    "Elements recorded. Please run comparison in PullRequest-For-Revit before syncing to central.",
                    "info"
                );
            }

            // Show TaskDialog if all recordings failed
            if (results.Count > 0 && results.All(r => !r.Success))
            {
                try
                {
                    TaskDialog.Show("PullRequest-For-Revit - Recording Failed",
                        $"Failed to record all selected elements.\n\nCheck logs for details.");
                }
                catch
                {
                    // If TaskDialog fails, just log
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in HandleRecordElements", ex);
            
            // Show TaskDialog for critical errors
            try
            {
                TaskDialog.Show("PullRequest-For-Revit - Error",
                    $"An error occurred during recording:\n\n{ex.Message}\n\nCheck logs for details.");
            }
            catch
            {
                // If TaskDialog fails, just log
            }

            var response = new RecordCompleteMessage
            {
                Success = false,
                Error = ex.Message
            };
            SendMessage(response);
        }
    }

    /// <summary>
    /// Core comparison logic shared between manual compare (from web UI)
    /// and automatic compare (from sync event).
    /// </summary>
    private (List<CompareResult> results, int changedCount, bool success, string? error)
        RunComparisonCore(UIApplication app)
    {
        var results = new List<CompareResult>();
        try
        {
            Logger.Instance.LogInfo("Running comparison for current session");

            var doc = app.ActiveUIDocument.Document;
            var view = doc.ActiveView;

            if (view == null)
            {
                Logger.Instance.LogWarning("RunComparisonCore: No active view");
                return (results, 0, false, "No active view");
            }

            // Always compare ALL recorded elements for the current session
            Logger.Instance.LogInfo("Comparing ALL recorded elements for current session (ignoring selection)");
            var recordedData = ElementComparer.LoadAllRecordedData(_dumpFolder);

            if (recordedData.Count == 0)
            {
                Logger.Instance.LogWarning("RunComparisonCore: No recorded data found");
                return (results, 0, false, "No recorded data found. Please record some elements first.");
            }

            Logger.Instance.LogInfo($"Comparing {recordedData.Count} recorded elements");

            // Get current elements for all recorded uniqueIds
            var currentElements = new Dictionary<string, Element>();
            foreach (var uniqueId in recordedData.Keys)
            {
                try
                {
                    Logger.Instance.LogInfo($"Looking for element with UniqueId: {uniqueId}");
                    var collector = new FilteredElementCollector(doc)
                        .WhereElementIsNotElementType()
                        .Where(e => e.UniqueId == uniqueId);

                    var element = collector.FirstOrDefault();
                    if (element != null)
                    {
                        currentElements[uniqueId] = element;
                        var displayName = GetElementDisplayName(element);
                        Logger.Instance.LogInfo($"Found element - UniqueId: {uniqueId}, Name: {displayName}");
                    }
                    else
                    {
                        Logger.Instance.LogWarning($"Element with UniqueId {uniqueId} not found in document");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error getting element {uniqueId}: {ex.Message}");
                }
            }

            // Compare elements
            foreach (var kvp in recordedData)
            {
                var uniqueId = kvp.Key;
                var recorded = kvp.Value;

                if (currentElements.TryGetValue(uniqueId, out var current))
                {
                    var result = ElementComparer.CompareElement(recorded, current);
                    results.Add(result);
                }
                else
                {
                    results.Add(new CompareResult
                    {
                        UniqueId = uniqueId,
                        HasChanges = true,
                        ChangeType = "deleted",
                        IsDeleted = true,
                        Error = "Element not found in current document"
                    });
                    Logger.Instance.LogWarning($"Element {recorded.Name} ({uniqueId}) was deleted - not found in current document");
                }
            }

            // Rank results: changed items first, then by change type and name
            results = results
                .OrderByDescending(r => r.HasChanges)
                .ThenBy(r => r.ChangeType)
                .ThenBy(r => r.Name)
                .ToList();

            var changedCount = results.Count(r => r.HasChanges);
            Logger.Instance.LogInfo($"Compare complete: {changedCount}/{results.Count} elements changed");

            // Try to create Revit viewport visualization (DirectContext3D)
            var visualizationCreated = ElementComparer.CreateVisualization(view, recordedData, currentElements, doc);
            if (!visualizationCreated)
            {
                Logger.Instance.LogWarning("RunComparisonCore: Failed to create Revit viewport visualization. Showing changes in web viewer only.");
            }

            return (results, changedCount, true, visualizationCreated ? null :
                "Failed to create Revit viewport visualization. Showing changes in web viewer only.");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in RunComparisonCore", ex);
            return (results, 0, false, ex.Message);
        }
    }

    private void HandleCompareElements(UIApplication app, CompareMessage message)
    {
        try
        {
            Logger.Instance.LogInfo("Handling compare:elements message (manual from web UI)");

            var (results, changedCount, success, error) = RunComparisonCore(app);

            if (!success && error != null)
            {
                var errorResponse = new CompareCompleteMessage
                {
                    Success = false,
                    Error = error
                };
                SendMessage(errorResponse);

                // Show TaskDialog for critical errors
                try
                {
                    TaskDialog.Show("PullRequest-For-Revit - Error",
                        $"An error occurred during comparison:\n\n{error}\n\nCheck logs for details.");
                }
                catch
                {
                    // If TaskDialog fails, just log
                }

                return;
            }

            var responseMessage = new CompareCompleteMessage
            {
                Success = true,
                Results = results,
                VisualizationCreated = true,
                Error = error
            };

            SendMessage(responseMessage);

            // Sync guard behavior:
            // - If there ARE changes, require user review/approval before sync.
            // - If there are NO changes, allow sync immediately (nothing to review).
            if (changedCount > 0)
            {
                SyncGuard.RequireConfirmation();
                SendNotification($"Change detected in {changedCount}/{results.Count} elements.", "warning");
            }
            else
            {
                SyncGuard.ConfirmAll();
                SendNotification("No changes detected in recorded elements.", "info");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in HandleCompareElements", ex);

            try
            {
                TaskDialog.Show("PullRequest-For-Revit - Error",
                    $"An error occurred during comparison:\n\n{ex.Message}\n\nCheck logs for details.");
            }
            catch
            {
                // ignore
            }

            var response = new CompareCompleteMessage
            {
                Success = false,
                Error = ex.Message
            };
            SendMessage(response);
        }
    }

    /// <summary>
    /// Run comparison from a sync attempt. This updates the web UI but does NOT
    /// change SyncGuard approval state; it only reports how many elements changed.
    /// </summary>
    public int RunAutomaticCompare(UIApplication app)
    {
        try
        {
            Logger.Instance.LogInfo("Running automatic compare triggered by sync event");

            var (results, changedCount, success, error) = RunComparisonCore(app);

            var responseMessage = new CompareCompleteMessage
            {
                Success = success,
                Results = results,
                VisualizationCreated = true,
                Error = error
            };

            SendMessage(responseMessage);

            return success ? changedCount : -1;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in RunAutomaticCompare", ex);
            return -1;
        }
    }

    private void HandleClearVisualization(UIApplication app)
    {
        try
        {
            Logger.Instance.LogInfo("Handling clear:visualization message");

            var doc = app.ActiveUIDocument.Document;
            var view = doc.ActiveView;

            if (view != null)
            {
                var removed = ElementComparer.RemoveVisualization(view);
                if (removed)
                {
                    Logger.Instance.LogInfo("Visualization cleared successfully");
                }
                else
                {
                    Logger.Instance.LogWarning("No visualization to clear");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in HandleClearVisualization", ex);
        }
    }

    private void HandleGetSelection(UIApplication app)
    {
        try
        {
            Logger.Instance.LogDebug("Handling get:selection message");

            if (app?.ActiveUIDocument?.Document == null)
            {
                Logger.Instance.LogWarning("HandleGetSelection: No active document");
                var emptyMessage = new SelectionChangedMessage { Elements = new List<ElementInfo>() };
                SendMessage(emptyMessage);
                return;
            }

            var doc = app.ActiveUIDocument.Document;
            var selection = app.ActiveUIDocument.Selection;
            
            if (selection == null)
            {
                Logger.Instance.LogWarning("HandleGetSelection: Selection is null");
                var emptyMessage = new SelectionChangedMessage { Elements = new List<ElementInfo>() };
                SendMessage(emptyMessage);
                return;
            }

            var selectedIds = selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                var emptyMessage = new SelectionChangedMessage { Elements = new List<ElementInfo>() };
                SendMessage(emptyMessage);
                Logger.Instance.LogDebug("Sent empty selection");
                return;
            }

            var elements = new List<ElementInfo>();

            foreach (ElementId id in selectedIds)
            {
                try
                {
                    var element = doc.GetElement(id);
                    if (element != null)
                    {
                    var uniqueId = element.UniqueId ?? string.Empty;
                    var displayName = GetElementDisplayName(element);
                    var categoryName = GetElementCategoryName(element);
                    elements.Add(new ElementInfo
                    {
                        Id = uniqueId,
                        Name = displayName,
                        Category = categoryName
                    });
                    
                    Logger.Instance.LogInfo($"GetSelection - UniqueId: {uniqueId}, Name: {displayName}, Category: {categoryName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogDebug($"Element {id} not accessible: {ex.Message}");
                }
            }

            var message = new SelectionChangedMessage
            {
                Elements = elements
            };

            SendMessage(message);
            Logger.Instance.LogDebug($"Sent selection: {elements.Count} elements");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in HandleGetSelection", ex);
            // Send empty selection on error
            try
            {
                var emptyMessage = new SelectionChangedMessage { Elements = new List<ElementInfo>() };
                SendMessage(emptyMessage);
            }
            catch { }
        }
    }

    private void HandleGetParquetFiles()
    {
        try
        {
            Logger.Instance.LogInfo("Handling parquet:get:files message");
            var files = ParquetReader.GetParquetFiles(_parquetFolder);
            
            var response = new ParquetFilesResponse
            {
                Files = files
            };
            
            SendMessage(response);
            Logger.Instance.LogInfo($"Sent {files.Count} parquet files");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in HandleGetParquetFiles", ex);
            var response = new ParquetFilesResponse
            {
                Error = ex.Message
            };
            SendMessage(response);
        }
    }

    private async Task HandleGetParquetFileInfo(GetParquetFileInfoMessage message)
    {
        ParquetFileInfoResponse? response = null;
        try
        {
            if (string.IsNullOrEmpty(message.FileName))
            {
                Logger.Instance.LogWarning("HandleGetParquetFileInfo: FileName is null or empty");
                response = new ParquetFileInfoResponse
                {
                    Error = "File name is required"
                };
                SendMessage(response);
                return;
            }

            // Security: Validate file name to prevent path traversal
            if (message.FileName.Contains("..") || Path.IsPathRooted(message.FileName))
            {
                Logger.Instance.LogWarning($"HandleGetParquetFileInfo: Invalid file name: {message.FileName}");
                response = new ParquetFileInfoResponse
                {
                    Error = "Invalid file name"
                };
                SendMessage(response);
                return;
            }

            Logger.Instance.LogInfo($"Handling parquet:get:info message for {message.FileName}");
            var filePath = Path.Combine(_parquetFolder, message.FileName);
            
            // Additional security check
            if (!filePath.StartsWith(_parquetFolder, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Instance.LogWarning($"HandleGetParquetFileInfo: Path traversal attempt: {filePath}");
                response = new ParquetFileInfoResponse
                {
                    Error = "Invalid file path"
                };
                SendMessage(response);
                return;
            }
            
            var info = await ParquetReader.GetFileInfoAsync(filePath);
            
            response = new ParquetFileInfoResponse
            {
                Info = info
            };
            
            if (info == null)
            {
                response.Error = "File not found or could not be read";
            }
            
            SendMessage(response);
            Logger.Instance.LogInfo($"Sent file info for {message.FileName}");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error in HandleGetParquetFileInfo for {message.FileName}", ex);
            response = new ParquetFileInfoResponse
            {
                Error = ex.Message
            };
            SendMessage(response);
        }
    }

    private async Task HandleGetParquetData(GetParquetDataMessage message)
    {
        ParquetDataResponse? response = null;
        try
        {
            if (string.IsNullOrEmpty(message.FileName))
            {
                Logger.Instance.LogWarning("HandleGetParquetData: FileName is null or empty");
                response = new ParquetDataResponse
                {
                    Rows = new List<Dictionary<string, object?>>(),
                    Columns = new List<string>(),
                    TotalRows = 0,
                    Offset = message.Offset,
                    Limit = message.Limit,
                    Error = "File name is required"
                };
                SendMessage(response);
                return;
            }

            // Security: Validate file name to prevent path traversal
            if (message.FileName.Contains("..") || Path.IsPathRooted(message.FileName))
            {
                Logger.Instance.LogWarning($"HandleGetParquetData: Invalid file name: {message.FileName}");
                response = new ParquetDataResponse
                {
                    Rows = new List<Dictionary<string, object?>>(),
                    Columns = new List<string>(),
                    TotalRows = 0,
                    Offset = message.Offset,
                    Limit = message.Limit,
                    Error = "Invalid file name"
                };
                SendMessage(response);
                return;
            }

            Logger.Instance.LogInfo($"Handling parquet:get:data message for {message.FileName} (offset: {message.Offset}, limit: {message.Limit})");
            var filePath = Path.Combine(_parquetFolder, message.FileName);
            
            // Additional security check
            if (!filePath.StartsWith(_parquetFolder, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Instance.LogWarning($"HandleGetParquetData: Path traversal attempt: {filePath}");
                response = new ParquetDataResponse
                {
                    Rows = new List<Dictionary<string, object?>>(),
                    Columns = new List<string>(),
                    TotalRows = 0,
                    Offset = message.Offset,
                    Limit = message.Limit,
                    Error = "Invalid file path"
                };
                SendMessage(response);
                return;
            }
            
            var result = await ParquetReader.ReadDataAsync(
                filePath, 
                message.Offset, 
                message.Limit, 
                message.SortColumn, 
                message.Ascending
            );
            
            response = new ParquetDataResponse
            {
                Rows = result?.Rows ?? new List<Dictionary<string, object?>>(),
                Columns = result?.Columns ?? new List<string>(),
                TotalRows = result?.TotalRows ?? 0,
                Offset = message.Offset,
                Limit = message.Limit
            };
            
            if (result == null)
            {
                response.Error = "File not found or could not be read";
            }
            
            SendMessage(response);
            Logger.Instance.LogInfo($"Sent {response.Rows.Count} rows for {message.FileName}");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error in HandleGetParquetData for {message.FileName}", ex);
            response = new ParquetDataResponse
            {
                Rows = new List<Dictionary<string, object?>>(),
                Columns = new List<string>(),
                TotalRows = 0,
                Offset = message.Offset,
                Limit = message.Limit,
                Error = ex.Message
            };
            SendMessage(response);
        }
    }

    private void OnSelectionChanged(object? sender, Autodesk.Revit.UI.Events.SelectionChangedEventArgs e)
    {
        try
        {
            // Check if WebView is ready before processing
            if (_webView?.CoreWebView2 == null)
            {
                Logger.Instance.LogDebug("OnSelectionChanged: WebView not ready, skipping");
                return;
            }

            var doc = e.GetDocument();
            if (doc == null)
            {
                Logger.Instance.LogWarning("OnSelectionChanged: Document is null");
                return;
            }

            var selectedElementIds = e.GetSelectedElements();
            if (selectedElementIds == null || selectedElementIds.Count == 0)
            {
                // No selection - send empty selection message
                var message = new SelectionChangedMessage
                {
                    Elements = new List<ElementInfo>()
                };
                SendMessage(message);
                Logger.Instance.LogDebug("Selection changed: No elements selected");
                return;
            }

            var elements = new List<ElementInfo>();
            
            // Process each element ID individually to handle deleted elements
            foreach (var elementId in selectedElementIds)
            {
                try
                {
                    if (elementId == null || elementId == ElementId.InvalidElementId)
                        continue;

                    var element = doc.GetElement(elementId);
                    if (element == null) continue;
                    
                    // Safely get UniqueId - it should never be null, but be defensive
                    var uniqueId = element.UniqueId;
                    if (string.IsNullOrEmpty(uniqueId))
                    {
                        Logger.Instance.LogWarning($"Element {elementId} has null/empty UniqueId");
                        continue;
                    }
                    
                    var displayName = GetElementDisplayName(element);
                    var categoryName = GetElementCategoryName(element);
                    elements.Add(new ElementInfo
                    {
                        Id = uniqueId,
                        Name = displayName,
                        Category = categoryName
                    });
                    
                    Logger.Instance.LogInfo($"Selected element - UniqueId: {uniqueId}, Name: {displayName}, Category: {categoryName}");
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    // Element might have been deleted
                    Logger.Instance.LogDebug($"Element {elementId} invalid: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Element might have been deleted or is invalid
                    Logger.Instance.LogDebug($"Element {elementId} not accessible: {ex.Message}");
                }
            }

            var message2 = new SelectionChangedMessage
            {
                Elements = elements
            };

            SendMessage(message2);
            Logger.Instance.LogDebug($"Selection changed: {elements.Count} elements selected");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in OnSelectionChanged", ex);
        }
    }

    /// <summary>
    /// Gets a safe display name for an element, handling cases where name or category might be null/empty
    /// </summary>
    private static string GetElementDisplayName(Element element)
    {
        if (element == null) return "Unknown Element";
        
        // Try to get element name
        var name = element.Name;
        if (!string.IsNullOrEmpty(name)) return name;
        
        // If no name, try to get category name
        var category = element.Category;
        if (category != null && !string.IsNullOrEmpty(category.Name))
        {
            return $"{category.Name} (No Name)";
        }
        
        // If no category, try to get element type name (for FamilyInstance)
        if (element is FamilyInstance familyInstance)
        {
            try
            {
                var elementType = familyInstance.Document.GetElement(familyInstance.GetTypeId()) as ElementType;
                if (elementType != null && !string.IsNullOrEmpty(elementType.Name))
                {
                    return $"{elementType.Name} (No Name)";
                }
            }
            catch { }
        }
        
        // Last resort: use element ID
        return $"Element {element.Id}";
    }
    
    /// <summary>
    /// Gets a safe category name for an element
    /// </summary>
    private static string GetElementCategoryName(Element element)
    {
        if (element == null) return "No Category";
        
        var category = element.Category;
        if (category != null && !string.IsNullOrEmpty(category.Name))
        {
            return category.Name;
        }
        
        return "No Category";
    }

    private void SendMessage(object message)
    {
        try
        {
            if (_webView == null)
            {
                Logger.Instance.LogWarning("WebView is null, message not sent");
                return;
            }

            if (_webView.CoreWebView2 == null)
            {
                Logger.Instance.LogWarning("WebView CoreWebView2 is null, message not sent");
                return;
            }

            var json = JsonConvert.SerializeObject(message, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore
            });
            
            _webView.CoreWebView2.PostWebMessageAsJson(json);
            Logger.Instance.LogDebug($"Sent message to frontend: {message.GetType().Name}");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error sending message to frontend", ex);
        }
    }

    private void SendNotification(string message, string level = "info")
    {
        var notification = new NotificationMessage
        {
            Level = level,
            Message = message
        };
        SendMessage(notification);
    }

    public async void InitializeWebViewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PullRequestForRevit");

            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);

            if (_webView != null)
            {
                await _webView.EnsureCoreWebView2Async(environment);
                var settings = _webView.CoreWebView2.Settings;
                settings.AreDevToolsEnabled = true;

                _webView.Source = new Uri(FrontendUrl);

                Logger.Instance.LogInfo($"WebView initialized, connecting to {FrontendUrl}");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error initializing WebView", ex);
        }
    }

    private void CleanupOldSessionFolders(string baseDumpFolder)
    {
        try
        {
            if (!Directory.Exists(baseDumpFolder))
            {
                return;
            }

            var sessionFolders = Directory.GetDirectories(baseDumpFolder, "session_*", SearchOption.TopDirectoryOnly);
            if (sessionFolders.Length == 0)
            {
                return;
            }

            Logger.Instance.LogInfo($"Found {sessionFolders.Length} session folders to check for cleanup");

            var now = DateTime.Now;
            var foldersToDelete = new List<string>();
            var folderInfo = new List<(string path, DateTime lastWrite, int fileCount)>();

            // Collect information about each session folder
            foreach (var folder in sessionFolders)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(folder);
                    var lastWrite = dirInfo.LastWriteTime;
                    var fileCount = dirInfo.GetFiles("*.json", SearchOption.TopDirectoryOnly).Length;
                    var age = now - lastWrite;

                    folderInfo.Add((folder, lastWrite, fileCount));

                    // Mark for deletion if older than max age
                    if (age.TotalDays > MaxSessionAgeDays)
                    {
                        foldersToDelete.Add(folder);
                        Logger.Instance.LogInfo($"Session folder marked for deletion (age: {age.TotalDays:F1} days): {Path.GetFileName(folder)}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error checking session folder {folder}: {ex.Message}");
                }
            }

            // If we have more than MaxSessionsToKeep, delete oldest ones (excluding current session)
            if (folderInfo.Count > MaxSessionsToKeep)
            {
                // Sort by last write time (oldest first)
                var sortedFolders = folderInfo
                    .OrderBy(f => f.lastWrite)
                    .Take(folderInfo.Count - MaxSessionsToKeep)
                    .Select(f => f.path);

                foreach (var folder in sortedFolders)
                {
                    if (!foldersToDelete.Contains(folder))
                    {
                        foldersToDelete.Add(folder);
                        Logger.Instance.LogInfo($"Session folder marked for deletion (exceeds max count): {Path.GetFileName(folder)}");
                    }
                }
            }

            // Delete marked folders
            int deletedCount = 0;
            foreach (var folder in foldersToDelete)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    deletedCount++;
                    Logger.Instance.LogInfo($"Deleted old session folder: {Path.GetFileName(folder)}");
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error deleting session folder {folder}: {ex.Message}");
                }
            }

            if (deletedCount > 0)
            {
                Logger.Instance.LogInfo($"Cleaned up {deletedCount} old session folder(s)");
            }
            else
            {
                Logger.Instance.LogInfo("No old session folders to clean up");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error during session folder cleanup", ex);
        }
    }
}

