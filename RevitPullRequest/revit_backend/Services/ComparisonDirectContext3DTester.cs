using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using PullRequestForRevit.Models;

namespace PullRequestForRevit.Services;

// Test version of ComparisonDirectContext3D without interface implementation
// This allows us to test the geometry building code separately
// while we resolve the ExternalServiceId interface issue
public class ComparisonDirectContext3DTester
{
    private readonly Dictionary<string, ElementData> _recordedData;
    private readonly Dictionary<string, Element> _currentElements;
    private readonly Document _document;
    private bool _geometryBuilt = false;
    private int _recordedVertexCount = 0;
    private int _recordedTriangleCount = 0;
    private int _currentVertexCount = 0;
    private int _currentTriangleCount = 0;

    public ComparisonDirectContext3DTester(Dictionary<string, ElementData> recordedData, Dictionary<string, Element> currentElements, Document document)
    {
        _recordedData = recordedData;
        _currentElements = currentElements;
        _document = document;
    }

    // Test method to build and validate geometry
    public bool TestBuildGeometry()
    {
        try
        {
            BuildGeometry();
            
            // Validate results - check if geometry data was built successfully
            bool hasRecordedGeometry = _recordedVertexCount > 0 && _recordedTriangleCount > 0;
            bool hasCurrentGeometry = _currentVertexCount > 0 && _currentTriangleCount > 0;
            
            Logger.Instance.LogInfo($"Geometry build test complete:");
            Logger.Instance.LogInfo($"  - Recorded geometry: {(hasRecordedGeometry ? "SUCCESS" : "FAILED")} ({_recordedVertexCount} vertices, {_recordedTriangleCount} triangles)");
            Logger.Instance.LogInfo($"  - Current geometry: {(hasCurrentGeometry ? "SUCCESS" : "FAILED")} ({_currentVertexCount} vertices, {_currentTriangleCount} triangles)");
            Logger.Instance.LogInfo($"  - Geometry built flag: {_geometryBuilt}");
            
            return hasRecordedGeometry || hasCurrentGeometry;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in TestBuildGeometry", ex);
            return false;
        }
    }

    // Get geometry statistics for debugging
    public string GetGeometryStats()
    {
        if (!_geometryBuilt)
        {
            return "Geometry not built yet";
        }

        return $"Recorded: {_recordedVertexCount} vertices, {_recordedTriangleCount} triangles | " +
               $"Current: {_currentVertexCount} vertices, {_currentTriangleCount} triangles";
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
                        Logger.Instance.LogInfo($"Added mesh geometry for element: {elementData.Name} ({elementData.MeshGeometry.Vertices.Count} vertices, {elementData.MeshGeometry.Faces.Count / 3} triangles)");
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
            int currentElementCount = 0;
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

                        int verticesBefore = currentVertices.Count;
                        int indicesBefore = currentIndices.Count;
                        
                        AddTrianglesFromGeometry(geometry, transform, currentVertices, currentIndices, new Color(255, 0, 0));
                        
                        if (currentVertices.Count > verticesBefore || currentIndices.Count > indicesBefore)
                        {
                            currentElementCount++;
                            Logger.Instance.LogInfo($"Added geometry for current element: {element.Name}");
                        }
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
                        currentElementCount++;
                        Logger.Instance.LogInfo($"Used bounding box for current element: {element.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogWarning($"Error extracting geometry for element {element.UniqueId}: {ex.Message}");
                }
            }

            // Validate geometry data (buffer creation will be done in actual implementation)
            // For testing, we just validate that we have valid geometry data
            if (recordedVertices.Count > 0 && recordedIndices.Count > 0)
            {
                // Validate all indices are within bounds
                int maxIndex = recordedVertices.Count - 1;
                int invalidIndices = 0;
                for (int i = 0; i < recordedIndices.Count; i++)
                {
                    if (recordedIndices[i] < 0 || recordedIndices[i] > maxIndex)
                    {
                        invalidIndices++;
                    }
                }
                
                if (invalidIndices > 0)
                {
                    Logger.Instance.LogWarning($"Found {invalidIndices} invalid indices in recorded geometry");
                }
                else
                {
                    Logger.Instance.LogInfo($"Recorded geometry validation passed: {recordedVertices.Count} vertices, {recordedIndices.Count / 3} triangles");
                }
            }
            else
            {
                Logger.Instance.LogWarning($"No recorded geometry to validate: {recordedVertices.Count} vertices, {recordedIndices.Count} indices");
            }

            if (currentVertices.Count > 0 && currentIndices.Count > 0)
            {
                // Validate all indices are within bounds
                int maxIndex = currentVertices.Count - 1;
                int invalidIndices = 0;
                for (int i = 0; i < currentIndices.Count; i++)
                {
                    if (currentIndices[i] < 0 || currentIndices[i] > maxIndex)
                    {
                        invalidIndices++;
                    }
                }
                
                if (invalidIndices > 0)
                {
                    Logger.Instance.LogWarning($"Found {invalidIndices} invalid indices in current geometry");
                }
                else
                {
                    Logger.Instance.LogInfo($"Current geometry validation passed: {currentVertices.Count} vertices, {currentIndices.Count / 3} triangles");
                }
            }
            else
            {
                Logger.Instance.LogWarning($"No current geometry to validate: {currentVertices.Count} vertices, {currentIndices.Count} indices");
            }
            
            // Track geometry counts (buffer creation will be done in actual implementation)
            // For testing, we validate that geometry data was built successfully
            _recordedVertexCount = recordedVertices.Count;
            _recordedTriangleCount = recordedIndices.Count / 3;
            _currentVertexCount = currentVertices.Count;
            _currentTriangleCount = currentIndices.Count / 3;
            
            Logger.Instance.LogInfo($"Geometry data prepared: Recorded ({_recordedVertexCount} vertices, {_recordedTriangleCount} triangles), " +
                                   $"Current ({_currentVertexCount} vertices, {_currentTriangleCount} triangles)");

            _geometryBuilt = true;
            Logger.Instance.LogInfo($"Built DirectContext3D geometry: {recordedElementCount} recorded elements ({recordedVertices.Count} vertices, {recordedIndices.Count / 3} triangles), " +
                                   $"{currentElementCount} current elements ({currentVertices.Count} vertices, {currentIndices.Count / 3} triangles)");
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
                    // Create ColorWithTransparency with 50% opacity (128/255)
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
            int validTriangles = 0;
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
                validTriangles++;
            }

            Logger.Instance.LogInfo($"Added {validTriangles}/{triangleCount} valid triangles from mesh data ({meshData.Vertices.Count} vertices)");
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
                            // Create ColorWithTransparency with 50% opacity (128/255)
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

        // Create ColorWithTransparency with 50% opacity (128/255)
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

