using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using PullRequestForRevit.Models;
using PullRequestForRevit.Services;
using Newtonsoft.Json;

namespace PullRequestForRevit.Services;

public static class ElementRecorder
{
    private static readonly Dictionary<string, ElementData> _cache = new();

    public static ElementData? RecordElement(Element element, Document document)
    {
        try
        {
            Logger.Instance.LogDebug($"Recording element: {element.UniqueId} - {element.Name}");

            var data = ElementData.FromElement(element, document);
            if (data == null)
            {
                Logger.Instance.LogWarning($"Failed to create ElementData for element: {element.UniqueId}");
                return null;
            }

            // Extract geometry
            var options = new Options
            {
                ComputeReferences = true,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };

            var geometryElement = element.get_Geometry(options);
            if (geometryElement != null)
            {
                var meshData = ExtractMeshGeometry(geometryElement);
                data.MeshGeometry = meshData;
            }

            // Cache in memory
            _cache[element.UniqueId] = data;

            Logger.Instance.LogDebug($"Successfully recorded element: {element.UniqueId}");
            return data;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error recording element {element.UniqueId}", ex);
            return null;
        }
    }

    private static MeshGeometryData? ExtractMeshGeometry(GeometryElement geometryElement)
    {
        try
        {
            var meshData = new MeshGeometryData
            {
                Vertices = new List<XYZData>(),
                Faces = new List<int>(),
                Normals = new List<XYZData>()
            };

            var vertexMap = new Dictionary<XYZ, int>();
            int vertexIndex = 0;

            foreach (GeometryObject geomObj in geometryElement)
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
                        var nestedMesh = ExtractMeshGeometry(instanceGeometry);
                        if (nestedMesh != null)
                        {
                            // Merge nested geometry
                            foreach (var v in nestedMesh.Vertices)
                            {
                                var xyz = v.ToXYZ();
                                if (!vertexMap.ContainsKey(xyz))
                                {
                                    vertexMap[xyz] = vertexIndex++;
                                    meshData.Vertices.Add(v);
                                }
                            }
                            meshData.Faces.AddRange(nestedMesh.Faces);
                            meshData.Normals.AddRange(nestedMesh.Normals);
                        }
                    }
                }
            }

            return meshData.Vertices.Count > 0 ? meshData : null;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting mesh geometry", ex);
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
            Logger.Instance.LogError("Error extracting solid geometry", ex);
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
                // Use a tolerance for vertex comparison
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

                // Calculate normal
                var normal = (v1 - v0).CrossProduct(v2 - v0).Normalize();
                meshData.Normals.Add(new XYZData(normal));
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting mesh geometry", ex);
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

                // Create a line segment (two triangles for visualization)
                meshData.Faces.Add(p0);
                meshData.Faces.Add(p1);
                meshData.Faces.Add(p0); // Degenerate triangle for line
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error extracting curve geometry", ex);
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

    public static bool SaveToFile(ElementData data, string dumpFolder)
    {
        try
        {
            if (!Directory.Exists(dumpFolder))
            {
                Directory.CreateDirectory(dumpFolder);
                Logger.Instance.LogInfo($"Created session dump folder: {dumpFolder}");
            }

            // Sanitize UniqueId for filename (replace invalid characters)
            var safeFileName = SanitizeFileName(data.UniqueId);
            var filePath = Path.Combine(dumpFolder, $"{safeFileName}.json");

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, json);

            Logger.Instance.LogInfo($"Saved element data to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error saving element data to file for {data.UniqueId}", ex);
            return false;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        // Replace invalid filename characters with underscore
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = fileName;
        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return sanitized;
    }

    public static ElementData? LoadFromFile(string uniqueId, string dumpFolder)
    {
        try
        {
            var safeFileName = SanitizeFileName(uniqueId);
            var filePath = Path.Combine(dumpFolder, $"{safeFileName}.json");

            if (!File.Exists(filePath))
            {
                Logger.Instance.LogWarning($"File not found: {filePath}");
                return null;
            }

            var json = File.ReadAllText(filePath);
            var data = JsonConvert.DeserializeObject<ElementData>(json);

            if (data != null)
            {
                _cache[uniqueId] = data;
                Logger.Instance.LogDebug($"Loaded element data from: {filePath}");
            }

            return data;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error loading element data from file for {uniqueId}", ex);
            return null;
        }
    }

    public static ElementData? GetCached(string uniqueId)
    {
        return _cache.TryGetValue(uniqueId, out var data) ? data : null;
    }
}

