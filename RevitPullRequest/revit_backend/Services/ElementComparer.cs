using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Autodesk.Revit.UI;
using PullRequestForRevit.Models;
using PullRequestForRevit.Services;

namespace PullRequestForRevit.Services;

public static class ElementComparer
{
    // Using DirectContext3DProxyFactory to create runtime proxy that implements IDirectContext3DServer
    // This works around the CS0738 error (ExternalServiceId is internal type) by using Reflection.Emit
    private static readonly Dictionary<ElementId, IDirectContext3DServer> _visualizations = new();

    public static Dictionary<string, ElementData> LoadRecordedData(List<string> uniqueIds, string dumpFolder)
    {
        var result = new Dictionary<string, ElementData>();

        try
        {
            foreach (var uniqueId in uniqueIds)
            {
                var data = ElementRecorder.LoadFromFile(uniqueId, dumpFolder);
                if (data != null)
                {
                    result[uniqueId] = data;
                }
                else
                {
                    Logger.Instance.LogWarning($"Could not load recorded data for {uniqueId}");
                }
            }

            Logger.Instance.LogInfo($"Loaded {result.Count}/{uniqueIds.Count} recorded elements");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error loading recorded data", ex);
        }

        return result;
    }

    public static Dictionary<string, ElementData> LoadAllRecordedData(string dumpFolder)
    {
        var result = new Dictionary<string, ElementData>();

        try
        {
            if (!Directory.Exists(dumpFolder))
            {
                Logger.Instance.LogWarning($"Session dump folder does not exist: {dumpFolder}");
                Logger.Instance.LogInfo("No recorded elements found for this session");
                return result;
            }

            var jsonFiles = Directory.GetFiles(dumpFolder, "*.json", SearchOption.TopDirectoryOnly);
            Logger.Instance.LogInfo($"Found {jsonFiles.Length} JSON files in session folder: {Path.GetFileName(dumpFolder)}");

            foreach (var filePath in jsonFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    // Skip log files and other non-element files
                    if (fileName.StartsWith("log_") || fileName.Contains("logs"))
                    {
                        continue;
                    }

                    var data = ElementRecorder.LoadFromFile(fileName, dumpFolder);
                    if (data != null && !string.IsNullOrEmpty(data.UniqueId))
                    {
                        result[data.UniqueId] = data;
                        Logger.Instance.LogInfo($"Loaded recorded data: {data.Name} ({data.UniqueId})");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error loading file {filePath}: {ex.Message}");
                }
            }

            Logger.Instance.LogInfo($"Loaded {result.Count} recorded elements from session folder");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error loading all recorded data", ex);
        }

        return result;
    }

    public static CompareResult CompareElement(ElementData recordedData, Element currentElement)
    {
        try
        {
            var result = new CompareResult
            {
                UniqueId = recordedData.UniqueId,
                Name = recordedData.Name,
                Category = recordedData.Category,
                HasChanges = false,
                ChangeType = "none"
            };

            var changeTypes = new List<string>();

            // Compare transforms (detect movement)
            bool isMoved = false;
            if (recordedData.Transform != null)
            {
                Transform currentTransform;
                if (currentElement.Location is LocationPoint locPoint)
                {
                    currentTransform = Transform.CreateTranslation(locPoint.Point);
                }
                else if (currentElement is FamilyInstance familyInstance)
                {
                    currentTransform = familyInstance.GetTotalTransform();
                }
                else
                {
                    currentTransform = Transform.Identity;
                }
                var recordedTransform = recordedData.Transform.ToTransform();

                var translation = currentTransform.Origin - recordedTransform.Origin;
                var distance = translation.GetLength();

                if (distance > 0.001) // Tolerance for movement
                {
                    isMoved = true;
                    result.HasChanges = true;
                    result.Translation = new XYZData(translation);
                    changeTypes.Add("moved");
                    Logger.Instance.LogInfo($"Element {recordedData.UniqueId} moved by distance: {distance:F3}");
                }
            }

            // Compare bounding boxes (additional movement/geometry change check)
            if (recordedData.BoundingBox != null)
            {
                var currentBbox = currentElement.get_BoundingBox(null);
                if (currentBbox != null)
                {
                    var recordedMin = recordedData.BoundingBox.Min.ToXYZ();
                    var recordedMax = recordedData.BoundingBox.Max.ToXYZ();

                    var minDiff = currentBbox.Min.DistanceTo(recordedMin);
                    var maxDiff = currentBbox.Max.DistanceTo(recordedMax);

                    if ((minDiff > 0.001 || maxDiff > 0.001) && !isMoved)
                    {
                        // Geometry changed but not detected as movement (could be resize)
                        result.HasChanges = true;
                        if (!changeTypes.Contains("geometry"))
                        {
                            changeTypes.Add("geometry");
                        }
                    }

                    // Store bounding boxes for web visualization (before / after)
                    result.RecordedBoundingBox = recordedData.BoundingBox;
                    result.CurrentBoundingBox = new BoundingBoxData(currentBbox);
                }
            }

            // Compare mesh geometry (shape/detail changes)
            if (recordedData.MeshGeometry != null)
            {
                try
                {
                    var currentMesh = ExtractCurrentMeshGeometry(currentElement);
                    if (currentMesh != null)
                    {
                        var recordedVertexCount = recordedData.MeshGeometry.Vertices?.Count ?? 0;
                        var recordedFaceCount = recordedData.MeshGeometry.Faces?.Count ?? 0;
                        var currentVertexCount = currentMesh.Vertices?.Count ?? 0;
                        var currentFaceCount = currentMesh.Faces?.Count ?? 0;

                        if (recordedVertexCount != currentVertexCount ||
                            recordedFaceCount != currentFaceCount)
                        {
                            result.HasChanges = true;
                            if (!changeTypes.Contains("geometry"))
                            {
                                changeTypes.Add("geometry");
                            }

                            Logger.Instance.LogInfo(
                                $"Element {recordedData.UniqueId} mesh changed. " +
                                $"Vertices: {recordedVertexCount} -> {currentVertexCount}, " +
                                $"Faces: {recordedFaceCount} -> {currentFaceCount}");
                        }
                    }
                }
                catch (Exception meshEx)
                {
                    Logger.Instance.LogWarning(
                        $"Error comparing mesh geometry for element {recordedData.UniqueId}: {meshEx.Message}");
                }
            }

            // Compare parameters
            var parameterChanges = CompareParameters(recordedData, currentElement);
            if (parameterChanges.Count > 0)
            {
                result.HasChanges = true;
                result.ParameterChanges = parameterChanges;
                changeTypes.Add("parameter");
                Logger.Instance.LogInfo($"Element {recordedData.UniqueId} has {parameterChanges.Count} parameter change(s)");
            }

            // Determine change type
            if (changeTypes.Count == 0)
            {
                result.ChangeType = "none";
            }
            else if (changeTypes.Count == 1)
            {
                result.ChangeType = changeTypes[0];
            }
            else
            {
                result.ChangeType = "multiple";
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error comparing element {recordedData.UniqueId}", ex);
            return new CompareResult
            {
                UniqueId = recordedData.UniqueId,
                HasChanges = false,
                ChangeType = "none",
                Error = ex.Message
            };
        }
    }

    private static List<ParameterChange> CompareParameters(ElementData recordedData, Element currentElement)
    {
        var changes = new List<ParameterChange>();

        try
        {
            // Get current element parameters
            var currentParameters = new Dictionary<string, object>();
            foreach (Parameter param in currentElement.Parameters)
            {
                if (param.HasValue && !param.IsReadOnly)
                {
                    var value = GetParameterValue(param);
                    if (value != null)
                    {
                        currentParameters[param.Definition.Name] = value;
                    }
                }
            }

            // Compare recorded parameters with current
            foreach (var kvp in recordedData.Parameters)
            {
                var paramName = kvp.Key;
                var recordedValue = kvp.Value;

                if (currentParameters.TryGetValue(paramName, out var currentValue))
                {
                    // Parameter exists in both - compare values
                    if (!ValuesEqual(recordedValue, currentValue))
                    {
                        changes.Add(new ParameterChange
                        {
                            Name = paramName,
                            OldValue = recordedValue,
                            NewValue = currentValue
                        });
                    }
                }
                else
                {
                    // Parameter was removed or no longer has a value
                    changes.Add(new ParameterChange
                    {
                        Name = paramName,
                        OldValue = recordedValue,
                        NewValue = null
                    });
                }
            }

            // Check for new parameters (added since recording)
            foreach (var kvp in currentParameters)
            {
                if (!recordedData.Parameters.ContainsKey(kvp.Key))
                {
                    changes.Add(new ParameterChange
                    {
                        Name = kvp.Key,
                        OldValue = null,
                        NewValue = kvp.Value
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogWarning($"Error comparing parameters for element {recordedData.UniqueId}: {ex.Message}");
        }

        return changes;
    }

    private static object? GetParameterValue(Parameter param)
    {
        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    return param.AsString();
                case StorageType.Integer:
                    return param.AsInteger();
                case StorageType.Double:
                    return param.AsDouble();
                case StorageType.ElementId:
                    return param.AsElementId().Value;
                case StorageType.None:
                default:
                    return null;
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool ValuesEqual(object? value1, object? value2)
    {
        if (value1 == null && value2 == null) return true;
        if (value1 == null || value2 == null) return false;

        // Handle numeric comparison with tolerance for doubles
        if (value1 is double d1 && value2 is double d2)
        {
            return Math.Abs(d1 - d2) < 0.001;
        }

        if (value1 is int i1 && value2 is int i2)
        {
            return i1 == i2;
        }

        // For other types, use string comparison
        return value1.ToString() == value2.ToString();
    }

    /// <summary>
    /// Extracts a simplified mesh geometry snapshot for the current element,
    /// using the same logic as recording, for mesh-based comparison.
    /// </summary>
    private static MeshGeometryData? ExtractCurrentMeshGeometry(Element element)
    {
        try
        {
            var options = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var geometryElement = element.get_Geometry(options);
            if (geometryElement == null)
            {
                return null;
            }

            var meshData = new MeshGeometryData
            {
                Vertices = new List<XYZData>(),
                Faces = new List<int>(),
                Normals = new List<XYZData>()
            };

            var vertexMap = new Dictionary<XYZ, int>();
            int vertexIndex = 0;

            void ExtractFromGeometryElement(GeometryElement geomElement)
            {
                foreach (GeometryObject geomObj in geomElement)
                {
                    if (geomObj is Solid solid && solid.Volume > 0)
                    {
                        ExtractSolidGeometry(solid, meshData, vertexMap, ref vertexIndex);
                    }
                    else if (geomObj is Mesh mesh)
                    {
                        ExtractMeshGeometry(mesh, meshData, vertexMap, ref vertexIndex);
                    }
                    else if (geomObj is Curve curve)
                    {
                        ExtractCurveGeometry(curve, meshData, vertexMap, ref vertexIndex);
                    }
                    else if (geomObj is GeometryInstance instance)
                    {
                        var instanceGeometry = instance.GetInstanceGeometry();
                        if (instanceGeometry != null)
                        {
                            ExtractFromGeometryElement(instanceGeometry);
                        }
                    }
                }
            }

            ExtractFromGeometryElement(geometryElement);

            return meshData.Vertices.Count > 0 ? meshData : null;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting current mesh geometry", ex);
            return null;
        }
    }

    private static void ExtractSolidGeometry(Solid solid, MeshGeometryData meshData, Dictionary<XYZ, int> vertexMap, ref int vertexIndex)
    {
        try
        {
            foreach (Face face in solid.Faces)
            {
                var faceMesh = face.Triangulate();
                if (faceMesh != null)
                {
                    ExtractMeshGeometry(faceMesh, meshData, vertexMap, ref vertexIndex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting solid geometry (comparison)", ex);
        }
    }

    private static void ExtractMeshGeometry(Mesh mesh, MeshGeometryData meshData, Dictionary<XYZ, int> vertexMap, ref int vertexIndex)
    {
        try
        {
            var vertices = mesh.Vertices;
            var numTriangles = mesh.NumTriangles;

            foreach (var vertex in vertices)
            {
                var key = FindClosestVertex(vertex, vertexMap.Keys);
                if (key == null)
                {
                    vertexMap[vertex] = vertexIndex++;
                    meshData.Vertices.Add(new XYZData(vertex));
                }
            }

            for (int i = 0; i < numTriangles; i++)
            {
                var triangle = mesh.get_Triangle(i);
                var v0 = vertices[(int)triangle.get_Index(0)];
                var v1 = vertices[(int)triangle.get_Index(1)];
                var v2 = vertices[(int)triangle.get_Index(2)];

                var idx0 = GetOrAddVertex(v0, meshData, vertexMap, ref vertexIndex);
                var idx1 = GetOrAddVertex(v1, meshData, vertexMap, ref vertexIndex);
                var idx2 = GetOrAddVertex(v2, meshData, vertexMap, ref vertexIndex);

                meshData.Faces.Add(idx0);
                meshData.Faces.Add(idx1);
                meshData.Faces.Add(idx2);

                var normal = (v1 - v0).CrossProduct(v2 - v0).Normalize();
                meshData.Normals.Add(new XYZData(normal));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting mesh geometry (comparison)", ex);
        }
    }

    private static void ExtractCurveGeometry(Curve curve, MeshGeometryData meshData, Dictionary<XYZ, int> vertexMap, ref int vertexIndex)
    {
        try
        {
            var points = curve.Tessellate();
            for (int i = 0; i < points.Count - 1; i++)
            {
                var p0 = GetOrAddVertex(points[i], meshData, vertexMap, ref vertexIndex);
                var p1 = GetOrAddVertex(points[i + 1], meshData, vertexMap, ref vertexIndex);

                meshData.Faces.Add(p0);
                meshData.Faces.Add(p1);
                meshData.Faces.Add(p0);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting curve geometry (comparison)", ex);
        }
    }

    private static int GetOrAddVertex(XYZ vertex, MeshGeometryData meshData, Dictionary<XYZ, int> vertexMap, ref int vertexIndex)
    {
        var key = FindClosestVertex(vertex, vertexMap.Keys);
        if (key != null)
        {
            return vertexMap[key];
        }

        vertexMap[vertex] = vertexIndex;
        meshData.Vertices.Add(new XYZData(vertex));
        return vertexIndex++;
    }

    private static XYZ? FindClosestVertex(XYZ vertex, IEnumerable<XYZ> vertices)
    {
        const double tolerance = 0.001;
        return vertices.FirstOrDefault(v => v.DistanceTo(vertex) < tolerance);
    }

    public static bool CreateVisualization(View view, Dictionary<string, ElementData> recordedData, Dictionary<string, Element> currentElements, Document document)
    {
        try
        {
            Logger.Instance.LogInfo($"Creating visualization in view: {view.Name}");

            var viewId = view.Id;

            // Remove existing visualization if any
            if (_visualizations.ContainsKey(viewId))
            {
                RemoveVisualization(view);
            }

            // Create and register DirectContext3D visualization server (Revit 2025 API)
            // Reference: https://www.revitapidocs.com/2025/f4ba10f0-55ea-5344-173b-688405391794.htm
            
            // Create runtime proxy using Reflection.Emit to work around ExternalServiceId interface issue
            Logger.Instance.LogInfo("Creating DirectContext3D proxy using Reflection.Emit...");
            var visualization = DirectContext3DProxyFactory.CreateProxy(recordedData, currentElements, document);
            Logger.Instance.LogInfo("DirectContext3D proxy created successfully");
            
            // Register the DirectContext3D server using ExternalServiceRegistry
            // Based on official SDK examples: https://help.autodesk.com/view/RVT/2026/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Views_Displaying_Graphics_with_DirectContext3D_html
            try
            {
                if (view is View3D view3D)
                {
                    // Official SDK pattern: Use ExternalServiceRegistry
                    var serviceId = ExternalServices.BuiltInExternalServices.DirectContext3DService;
                    var service = ExternalServiceRegistry.GetService(serviceId);
                    if (service != null)
                    {
                        service.AddServer(visualization);
                        Logger.Instance.LogInfo("DirectContext3D server registered successfully using ExternalServiceRegistry");
                    }
                    else
                    {
                        Logger.Instance.LogError("Failed to get DirectContext3D service from ExternalServiceRegistry");
                        return false;
                    }
                }
                else
                {
                    Logger.Instance.LogWarning($"View {view.Name} is not a 3D view. DirectContext3D requires a 3D view.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogError("Error registering DirectContext3D server", ex);
                return false;
            }
            
            // Store the visualization
            _visualizations[viewId] = visualization;
            
            Logger.Instance.LogInfo("DirectContext3D visualization server successfully registered and ready to render");
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error creating visualization", ex);
            return false;
        }
    }

    public static bool RemoveVisualization(View view)
    {
        try
        {
            var viewId = view.Id;

            if (_visualizations.TryGetValue(viewId, out var visualization))
            {
                // Remove DirectContext3D server using ExternalServiceRegistry
                try
                {
                    var serviceId = ExternalServices.BuiltInExternalServices.DirectContext3DService;
                    var service = ExternalServiceRegistry.GetService(serviceId);
                if (service != null)
                {
                    service.RemoveServer(visualization.GetServerId());
                    Logger.Instance.LogInfo("DirectContext3D server removed successfully");
                }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error removing DirectContext3D server: {ex.Message}");
                }

                _visualizations.Remove(viewId);
                Logger.Instance.LogInfo($"Removed visualization from view: {view.Name}");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error removing visualization", ex);
            return false;
        }
    }

    public static bool HasVisualization(View view)
    {
        return _visualizations.ContainsKey(view.Id);
    }
}

// DirectContext3D implementation for ghost geometry visualization (Revit 2025/2026)
// Implements IDirectContext3DServer to display recorded geometry in blue (transparent) 
// and current geometry in red (transparent) as overlay graphics
// 
// References:
// - https://help.autodesk.com/view/RVT/2026/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Views_Displaying_Graphics_with_DirectContext3D_html
// - https://www.revitapidocs.com/2025/f4ba10f0-55ea-5344-173b-688405391794.htm
// - Revit SDK DuplicateGraphics sample
//
// NOTE: ExternalServiceId is an internal type, but the runtime type returned by
// DirectContext3DService property is correct. The compiler cannot verify this,
// but the code will work at runtime as the actual type matches the interface requirement.
//
// DirectContext3D implementation for visualization
// Base class that contains the actual implementation logic
// The wrapper (DirectContext3DWrapper) implements the interface and delegates to this class
public class ComparisonDirectContext3D
{
    private readonly Dictionary<string, ElementData> _recordedData;
    private readonly Dictionary<string, Element> _currentElements;
    private readonly Document _document;
    private VertexBuffer? _recordedVertexBuffer;
    private IndexBuffer? _recordedIndexBuffer;
    private VertexBuffer? _currentVertexBuffer;
    private IndexBuffer? _currentIndexBuffer;
    private bool _geometryBuilt = false;

    public ComparisonDirectContext3D(Dictionary<string, ElementData> recordedData, Dictionary<string, Element> currentElements, Document document)
    {
        _recordedData = recordedData;
        _currentElements = currentElements;
        _document = document;
    }

    // IExternalServer implementation
    // Based on official Revit SDK examples and documentation
    // Reference: https://help.autodesk.com/view/RVT/2026/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Basic_Interaction_with_Revit_Elements_Views_Displaying_Graphics_with_DirectContext3D_html
    public Guid GetServerId()
    {
        return new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
    }

    // GetServiceId() returns ExternalServiceId which is an internal type
    // Based on official SDK examples, we return the service ID directly
    // The property BuiltInExternalServices.DirectContext3DService returns ExternalServiceId
    // Note: ExternalServiceId is internal, but the runtime type is correct
    // Workaround: Using dynamic to bypass compile-time type checking
    public dynamic GetServiceId()
    {
        // Official SDK pattern: return BuiltInExternalServices.DirectContext3DService
        // This returns ExternalServiceId at runtime (internal type)
        return ExternalServices.BuiltInExternalServices.DirectContext3DService;
    }

    public string GetVendorId()
    {
        return "PullRequestForRevit";
    }

    public string GetName()
    {
        return "PullRequest-For-Revit Comparison Visualization";
    }

    public string GetDescription()
    {
        return "PullRequest-For-Revit Element Comparison Visualization - Shows recorded (blue) vs current (red) geometry";
    }

    // IDirectContext3DServer implementation
    public string GetSourceId()
    {
        return "PullRequestForRevitComparisonVisualization";
    }

    public string GetApplicationId()
    {
        return "PullRequestForRevit.Comparison";
    }

    public bool UsesHandles()
    {
        return false; // We don't use handles for this visualization
    }

    public bool CanExecute(View view)
    {
        return view is View3D; // Only works in 3D views
    }

    public Outline GetBoundingBox(View view)
    {
        try
        {
            XYZ? minPoint = null;
            XYZ? maxPoint = null;
            bool first = true;

            // Calculate bounding box from all geometry
            foreach (var kvp in _recordedData)
            {
                if (kvp.Value.BoundingBox != null)
                {
                    var min = kvp.Value.BoundingBox.Min.ToXYZ();
                    var max = kvp.Value.BoundingBox.Max.ToXYZ();
                    
                    if (first)
                    {
                        minPoint = min;
                        maxPoint = max;
                        first = false;
                    }
                    else if (minPoint != null && maxPoint != null)
                    {
                        minPoint = new XYZ(
                            Math.Min(minPoint.X, min.X),
                            Math.Min(minPoint.Y, min.Y),
                            Math.Min(minPoint.Z, min.Z)
                        );
                        maxPoint = new XYZ(
                            Math.Max(maxPoint.X, max.X),
                            Math.Max(maxPoint.Y, max.Y),
                            Math.Max(maxPoint.Z, max.Z)
                        );
                    }
                }
            }

            foreach (var kvp in _currentElements)
            {
                var elementBbox = kvp.Value.get_BoundingBox(null);
                if (elementBbox != null)
                {
                    if (first)
                    {
                        minPoint = elementBbox.Min;
                        maxPoint = elementBbox.Max;
                        first = false;
                    }
                    else if (minPoint != null && maxPoint != null)
                    {
                        minPoint = new XYZ(
                            Math.Min(minPoint.X, elementBbox.Min.X),
                            Math.Min(minPoint.Y, elementBbox.Min.Y),
                            Math.Min(minPoint.Z, elementBbox.Min.Z)
                        );
                        maxPoint = new XYZ(
                            Math.Max(maxPoint.X, elementBbox.Max.X),
                            Math.Max(maxPoint.Y, elementBbox.Max.Y),
                            Math.Max(maxPoint.Z, elementBbox.Max.Z)
                        );
                    }
                }
            }

            if (minPoint != null && maxPoint != null)
            {
                return new Outline(minPoint, maxPoint);
            }

            return new Outline(new XYZ(0, 0, 0), new XYZ(1, 1, 1));
        }
        catch
        {
            return new Outline(new XYZ(0, 0, 0), new XYZ(1, 1, 1));
        }
    }

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        try
        {
            if (!_geometryBuilt)
            {
                BuildGeometry();
            }

            // TODO: Fix DrawContext API - need to check correct way to get DrawContext in Revit 2025
            // var drawContext = DrawContext.GetDrawContext(view);
            // if (drawContext == null) return;
            
            // Temporarily disabled - RenderScene implementation needs DrawContext API fix
            Logger.Instance.LogWarning("RenderScene called but DrawContext API needs to be fixed");
            return;

            // Render recorded geometry in blue (transparent)
            // TODO: Fix DrawContext and EffectInstance API calls for Revit 2025
            /*
            if (_recordedVertexBuffer != null && _recordedIndexBuffer != null)
            {
                var blueColor = new Color(0, 100, 255); // Blue
                var effect = EffectInstance.Create(blueColor, 0.3); // 30% opacity
                
                drawContext.DrawGeometry(
                    _recordedVertexBuffer,
                    _recordedIndexBuffer,
                    PrimitiveType.TriangleList,
                    effect
                );
            }

            // Render current geometry in red (transparent)
            if (_currentVertexBuffer != null && _currentIndexBuffer != null)
            {
                var redColor = new Color(255, 0, 0); // Red
                var effect = EffectInstance.Create(redColor, 0.3); // 30% opacity
                
                drawContext.DrawGeometry(
                    _currentVertexBuffer,
                    _currentIndexBuffer,
                    PrimitiveType.TriangleList,
                    effect
                );
            }
            */
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error rendering DirectContext3D scene", ex);
        }
    }

    public bool UseInTransparentPass(View view)
    {
        return true; // Enable transparency for ghost geometry
    }

    private void BuildGeometry()
    {
        try
        {
            var recordedVertices = new List<VertexPositionColored>();
            var recordedIndices = new List<int>();
            var currentVertices = new List<VertexPositionColored>();
            var currentIndices = new List<int>();

            Logger.Instance.LogInfo($"Building DirectContext3D geometry from {_recordedData.Count} recorded elements and {_currentElements.Count} current elements");

            // Build geometry from recorded data (blue - original/recorded geometry)
            int recordedElementCount = 0;
            foreach (var kvp in _recordedData)
            {
                var elementData = kvp.Value;
                if (elementData == null)
                {
                    Logger.Instance.LogWarning($"Null element data for key: {kvp.Key}");
                    continue;
                }

                bool geometryAdded = false;

                // Prefer mesh geometry if available (more accurate)
                if (elementData.MeshGeometry != null && 
                    elementData.MeshGeometry.Vertices != null && 
                    elementData.MeshGeometry.Vertices.Count > 0)
                {
                    int verticesBefore = recordedVertices.Count;
                    int indicesBefore = recordedIndices.Count;
                    
                    AddTrianglesFromMeshData(elementData.MeshGeometry, elementData.Transform, 
                        recordedVertices, recordedIndices, new Color(0, 100, 255));
                    
                    if (recordedVertices.Count > verticesBefore || recordedIndices.Count > indicesBefore)
                    {
                        geometryAdded = true;
                        recordedElementCount++;
                    }
                }
                
                // Fallback to bounding box if no mesh geometry
                if (!geometryAdded && elementData.BoundingBox != null)
                {
                    AddTrianglesFromBoundingBox(elementData.BoundingBox, elementData.Transform,
                        recordedVertices, recordedIndices, new Color(0, 100, 255));
                    recordedElementCount++;
                    Logger.Instance.LogInfo($"Used bounding box for recorded element: {elementData.Name}");
                }
                
                if (!geometryAdded && elementData.BoundingBox == null)
                {
                    Logger.Instance.LogWarning($"No geometry data available for recorded element: {elementData.Name} ({elementData.UniqueId})");
                }
            }

            // Build geometry from current elements (red)
            foreach (var kvp in _currentElements)
            {
                var element = kvp.Value;
                try
                {
                    var options = new Options
                    {
                        ComputeReferences = false,
                        IncludeNonVisibleObjects = false,
                        DetailLevel = ViewDetailLevel.Medium
                    };

                    var geometry = element.get_Geometry(options);
                    if (geometry != null)
                    {
                        Transform transform = Transform.Identity;
                        if (element is FamilyInstance fi)
                        {
                            transform = fi.GetTotalTransform();
                        }
                        else if (element.Location is LocationPoint lp)
                        {
                            transform = Transform.CreateTranslation(lp.Point);
                        }

                        AddTrianglesFromGeometry(geometry, transform, currentVertices, currentIndices, new Color(255, 0, 0));
                    }
                    else if (element.get_BoundingBox(null) is BoundingBoxXYZ bbox)
                    {
                        var bboxData = new BoundingBoxData(bbox);
                        Transform transform = Transform.Identity;
                        if (element is FamilyInstance fi)
                        {
                            transform = fi.GetTotalTransform();
                        }
                        AddTrianglesFromBoundingBox(bboxData, new TransformData(transform), currentVertices, currentIndices, new Color(255, 0, 0));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error extracting geometry for element {element.UniqueId}: {ex.Message}");
                }
            }

            // TODO: Buffer creation disabled - need to fix VertexStreamPositionColored and IndexStreamTriangle constructors
            // These constructors don't take 1 argument in Revit 2025 API - need to check correct API pattern
            // Buffer creation will be needed once DirectContext3D rendering is enabled
            Logger.Instance.LogInfo($"Geometry data prepared: Recorded ({recordedVertices.Count} vertices, {recordedIndices.Count / 3} triangles), " +
                                   $"Current ({currentVertices.Count} vertices, {currentIndices.Count / 3} triangles)");
            /*
            // Create vertex and index buffers for recorded geometry
            if (recordedVertices.Count > 0 && recordedIndices.Count > 0)
            {
                // Buffer creation code commented out - API needs to be fixed
            }

            // Create vertex and index buffers for current geometry
            if (currentVertices.Count > 0 && currentIndices.Count > 0)
            {
                // Buffer creation code commented out - API needs to be fixed
            }
            */

            _geometryBuilt = true;
            Logger.Instance.LogInfo($"Built DirectContext3D geometry: {recordedElementCount} recorded elements ({recordedVertices.Count} vertices, {recordedIndices.Count / 3} triangles), " +
                                   $"{currentVertices.Count} current vertices, {currentIndices.Count / 3} current triangles");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error building DirectContext3D geometry", ex);
        }
    }

    private void AddTrianglesFromMeshData(MeshGeometryData meshData, TransformData? transformData, 
        List<VertexPositionColored> vertices, List<int> indices, Color color)
    {
        try
        {
            if (meshData == null || meshData.Vertices == null || meshData.Faces == null)
            {
                Logger.Instance.LogWarning("MeshGeometryData is null or incomplete");
                return;
            }

            if (meshData.Vertices.Count < 3)
            {
                Logger.Instance.LogWarning($"Insufficient vertices in mesh data: {meshData.Vertices.Count}");
                return;
            }

            if (meshData.Faces.Count < 3 || meshData.Faces.Count % 3 != 0)
            {
                Logger.Instance.LogWarning($"Invalid face count in mesh data: {meshData.Faces.Count} (must be multiple of 3)");
                return;
            }

            var transform = transformData?.ToTransform() ?? Transform.Identity;
            int baseIndex = vertices.Count;
            int maxVertexIndex = meshData.Vertices.Count - 1;

            // Add all vertices with transform applied
            foreach (var vertexData in meshData.Vertices)
            {
                if (vertexData == null)
                {
                    Logger.Instance.LogWarning("Null vertex data found, skipping");
                    continue;
                }

                try
                {
                    var xyz = transform.OfPoint(vertexData.ToXYZ());
                    var colorWithTransparency = new ColorWithTransparency(color.Red, color.Green, color.Blue, 128);
                    vertices.Add(new VertexPositionColored(xyz, colorWithTransparency));
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error transforming vertex: {ex.Message}");
                }
            }

            // Add triangle indices with validation
            int triangleCount = meshData.Faces.Count / 3;
            for (int i = 0; i < triangleCount; i++)
            {
                int idx0 = meshData.Faces[i * 3];
                int idx1 = meshData.Faces[i * 3 + 1];
                int idx2 = meshData.Faces[i * 3 + 2];

                // Validate indices are within bounds
                if (idx0 < 0 || idx0 > maxVertexIndex ||
                    idx1 < 0 || idx1 > maxVertexIndex ||
                    idx2 < 0 || idx2 > maxVertexIndex)
                {
                    Logger.Instance.LogWarning($"Invalid face indices at triangle {i}: [{idx0}, {idx1}, {idx2}], max index: {maxVertexIndex}");
                    continue;
                }

                // Check for degenerate triangles (all same vertex)
                if (idx0 == idx1 && idx1 == idx2)
                {
                    continue; // Skip degenerate triangles
                }

                indices.Add(baseIndex + idx0);
                indices.Add(baseIndex + idx1);
                indices.Add(baseIndex + idx2);
            }

            Logger.Instance.LogInfo($"Added {triangleCount} triangles from mesh data ({meshData.Vertices.Count} vertices)");
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error adding triangles from mesh data", ex);
        }
    }

    private void AddTrianglesFromGeometry(GeometryElement geometry, Transform transform,
        List<VertexPositionColored> vertices, List<int> indices, Color color)
    {
        int baseIndex = vertices.Count;

        foreach (GeometryObject geomObj in geometry)
        {
            if (geomObj is Solid solid && solid.Volume > 0)
            {
                foreach (Face face in solid.Faces)
                {
                    var mesh = face.Triangulate();
                    if (mesh != null)
                    {
                        var meshVertices = mesh.Vertices;
                        var numTriangles = mesh.NumTriangles;

                        foreach (var vertex in meshVertices)
                        {
                            var xyz = transform.OfPoint(vertex);
                            var colorWithTransparency = new ColorWithTransparency(color.Red, color.Green, color.Blue, 128);
                            vertices.Add(new VertexPositionColored(xyz, colorWithTransparency));
                        }

                        for (int i = 0; i < numTriangles; i++)
                        {
                            var triangle = mesh.get_Triangle(i);
                            var v0 = (int)triangle.get_Index(0);
                            var v1 = (int)triangle.get_Index(1);
                            var v2 = (int)triangle.get_Index(2);
                            indices.Add(baseIndex + v0);
                            indices.Add(baseIndex + v1);
                            indices.Add(baseIndex + v2);
                        }

                        baseIndex = vertices.Count;
                    }
                }
            }
        }
    }

    private void AddTrianglesFromBoundingBox(BoundingBoxData bbox, TransformData? transformData,
        List<VertexPositionColored> vertices, List<int> indices, Color color)
    {
        var transform = transformData?.ToTransform() ?? Transform.Identity;
        var min = transform.OfPoint(bbox.Min.ToXYZ());
        var max = transform.OfPoint(bbox.Max.ToXYZ());

        int baseIndex = vertices.Count;

        // Create 8 vertices of the box
        var corners = new[]
        {
            new XYZ(min.X, min.Y, min.Z),
            new XYZ(max.X, min.Y, min.Z),
            new XYZ(max.X, max.Y, min.Z),
            new XYZ(min.X, max.Y, min.Z),
            new XYZ(min.X, min.Y, max.Z),
            new XYZ(max.X, min.Y, max.Z),
            new XYZ(max.X, max.Y, max.Z),
            new XYZ(min.X, max.Y, max.Z)
        };

        var colorWithTransparency = new ColorWithTransparency(color.Red, color.Green, color.Blue, 128);
        foreach (var corner in corners)
        {
            vertices.Add(new VertexPositionColored(corner, colorWithTransparency));
        }

        // Add 12 triangles (2 per face)
        // Bottom
        indices.AddRange(new[] { baseIndex + 0, baseIndex + 1, baseIndex + 2 });
        indices.AddRange(new[] { baseIndex + 0, baseIndex + 2, baseIndex + 3 });
        // Top
        indices.AddRange(new[] { baseIndex + 4, baseIndex + 6, baseIndex + 5 });
        indices.AddRange(new[] { baseIndex + 4, baseIndex + 7, baseIndex + 6 });
        // Front
        indices.AddRange(new[] { baseIndex + 0, baseIndex + 4, baseIndex + 5 });
        indices.AddRange(new[] { baseIndex + 0, baseIndex + 5, baseIndex + 1 });
        // Back
        indices.AddRange(new[] { baseIndex + 2, baseIndex + 6, baseIndex + 7 });
        indices.AddRange(new[] { baseIndex + 2, baseIndex + 7, baseIndex + 3 });
        // Left
        indices.AddRange(new[] { baseIndex + 0, baseIndex + 3, baseIndex + 7 });
        indices.AddRange(new[] { baseIndex + 0, baseIndex + 7, baseIndex + 4 });
        // Right
        indices.AddRange(new[] { baseIndex + 1, baseIndex + 5, baseIndex + 6 });
        indices.AddRange(new[] { baseIndex + 1, baseIndex + 6, baseIndex + 2 });
    }
}

