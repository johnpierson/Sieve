using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Events;
using PullRequestForRevit.Services;
using Nice3point.Revit.Toolkit.Decorators;
using Nice3point.Revit.Toolkit.External;
using Nice3point.Revit.Toolkit.External.Handlers;
using PullRequestForRevit.Commands;

namespace PullRequestForRevit;

/// <summary>
///     Application entry point
/// </summary>
[UsedImplicitly]
public class Application : ExternalApplication
{
    public static ActionEventHandler ActionEventHandler = new();
    public static Guid PullRequestForRevitDockablePaneId = new("B48349E5-4DF6-40EE-986E-9784D018E036");

    public override void OnStartup()
    {
        try
        {
            // Initialize logger
            Logger.Instance.LogInfo("PullRequestForRevit application starting up");

            CreateRibbon();
            CreateDockablePane();
            SubscribeToSyncEvents();

            Logger.Instance.LogInfo("PullRequestForRevit application startup complete");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogFatal("Failed to start PullRequestForRevit application", ex);
            throw;
        }
    }

    private void CreateRibbon()
    {
        try
        {
            var panel = Application.CreatePanel("PullRequest-For-Revit", "PullRequestForRevit");

            panel.AddPushButton<StartupCommand>("PullRequest-For-Revit")
                .SetImage("/PullRequestForRevit;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/PullRequestForRevit;component/Resources/Icons/RibbonIcon32.png");

            Logger.Instance.LogInfo("Ribbon panel created successfully");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Failed to create ribbon panel", ex);
            throw;
        }
    }

    private void CreateDockablePane()
    {
        try
        {
            if (!DockablePane.PaneIsRegistered(new DockablePaneId(PullRequestForRevitDockablePaneId)))
            {
                DockablePaneProvider
                    .Register(Context.UiControlledApplication, PullRequestForRevitDockablePaneId, "PullRequest-For-Revit")
                    .SetConfiguration((data) =>
                    {
                        data.FrameworkElement = new PullRequestForRevitView();
                        data.InitialState = new DockablePaneState
                        {
                            DockPosition = DockPosition.Tabbed,
                        };
                    });

                Logger.Instance.LogInfo("Dockable pane registered successfully");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Failed to create dockable pane", ex);
            throw;
        }
    }

    /// <summary>
    /// Subscribe to Revit synchronizing-with-central event so we can
    /// block sync until the user has confirmed changes in the web viewer.
    /// </summary>
    private void SubscribeToSyncEvents()
    {
        try
        {
            var controlledApp = Context.UiControlledApplication.ControlledApplication;
            controlledApp.DocumentSynchronizingWithCentral += OnDocumentSynchronizingWithCentral;
            Logger.Instance.LogInfo("Subscribed to DocumentSynchronizingWithCentral event");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Failed to subscribe to DocumentSynchronizingWithCentral", ex);
        }
    }

    private void OnDocumentSynchronizingWithCentral(object? sender, DocumentSynchronizingWithCentralEventArgs e)
    {
        try
        {
            Logger.Instance.LogInfo("DocumentSynchronizingWithCentral event fired");

            try
            {
                int changedCount = -1;

                ActionEventHandler.Raise(app =>
                {
                    if (PullRequestForRevitView.Instance != null)
                    {
                        changedCount = PullRequestForRevitView.Instance.RunAutomaticCompare(app);
                    }
                    else
                    {
                        Logger.Instance.LogWarning("PullRequestForRevitView.Instance is null; cannot run automatic compare from sync event.");
                    }
                });

                // If comparison failed, be safe and block sync
                if (changedCount < 0)
                {
                    e.Cancel();
                    TaskDialog.Show(
                        "PullRequest-For-Revit Sync Guard",
                        "Synchronization with central is blocked due to an error while checking changes.\n\n" +
                        "Please open the PullRequest-For-Revit panel and run comparison manually, then try to synchronize again.");
                    Logger.Instance.LogInfo("Sync canceled because automatic compare failed.");
                    return;
                }

                // No changes detected => allow sync directly
                if (changedCount == 0)
                {
                    Logger.Instance.LogInfo("Automatic compare found no changes; sync will proceed.");
                    return;
                }

                // Changes detected:
                // - If they have already been approved in the web UI (SyncGuard.CanSync == true),
                //   allow sync.
                // - Otherwise, block sync and show message.
                if (!SyncGuard.CanSync)
                {
                    e.Cancel();
                    TaskDialog.Show(
                        "PullRequest-For-Revit Sync Guard",
                        "Synchronization with central is blocked.\n\n" +
                        "Changes have been detected and are not yet approved.\n" +
                        "Please review and confirm changes in the PullRequest-For-Revit panel, then try to synchronize again.");
                    Logger.Instance.LogInfo("Sync canceled because changes are detected and not approved.");
                }
                else
                {
                    Logger.Instance.LogInfo("Changes detected but they have been approved; sync will proceed.");
                }
            }
            catch (Exception innerEx)
            {
                Logger.Instance.LogError("Error running automatic compare from sync event", innerEx);

                e.Cancel();
                TaskDialog.Show(
                    "PullRequest-For-Revit Sync Guard",
                    "Synchronization with central is blocked due to an error while checking changes.\n\n" +
                    "Please open the PullRequest-For-Revit panel and run comparison manually, then try to synchronize again.");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in OnDocumentSynchronizingWithCentral", ex);
        }
    }
}

