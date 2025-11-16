using System.Collections.Generic;
using Autodesk.Revit.DB;
using Newtonsoft.Json;

namespace PullRequestForRevit.Models;

public class ElementData
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("location")]
    public XYZData? Location { get; set; }

    [JsonProperty("transform")]
    public TransformData? Transform { get; set; }

    [JsonProperty("boundingBox")]
    public BoundingBoxData? BoundingBox { get; set; }

    [JsonProperty("meshGeometry")]
    public MeshGeometryData? MeshGeometry { get; set; }

    [JsonProperty("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();

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

    public static ElementData? FromElement(Element element, Document document)
    {
        try
        {
            var data = new ElementData
            {
                UniqueId = element.UniqueId,
                Name = GetElementDisplayName(element),
                Category = GetElementCategoryName(element),
                Parameters = new Dictionary<string, object>()
            };

            // Extract location and transform
            Autodesk.Revit.DB.Transform transform;
            if (element.Location is LocationPoint locPoint)
            {
                data.Location = new XYZData(locPoint.Point);
                transform = Autodesk.Revit.DB.Transform.CreateTranslation(locPoint.Point);
            }
            else if (element.Location is LocationCurve locCurve)
            {
                var curve = locCurve.Curve;
                data.Location = new XYZData(curve.GetEndPoint(0));
                transform = Autodesk.Revit.DB.Transform.CreateTranslation(curve.GetEndPoint(0));
            }
            else if (element is FamilyInstance familyInstance)
            {
                transform = familyInstance.GetTotalTransform();
                if (familyInstance.Location is LocationPoint fiLocPoint)
                {
                    data.Location = new XYZData(fiLocPoint.Point);
                }
            }
            else
            {
                transform = Autodesk.Revit.DB.Transform.Identity;
            }
            data.Transform = new TransformData(transform);

            // Extract bounding box
            var bbox = element.get_BoundingBox(null);
            if (bbox != null)
            {
                data.BoundingBox = new BoundingBoxData(bbox);
            }

            // Extract parameters
            foreach (Parameter param in element.Parameters)
            {
                if (param.HasValue && !param.IsReadOnly)
                {
                    var value = GetParameterValue(param);
                    if (value != null)
                    {
                        data.Parameters[param.Definition.Name] = value;
                    }
                }
            }

            return data;
        }
        catch
        {
            return null;
        }
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
}

public class XYZData
{
    [JsonProperty("x")]
    public double X { get; set; }

    [JsonProperty("y")]
    public double Y { get; set; }

    [JsonProperty("z")]
    public double Z { get; set; }

    public XYZData() { }

    public XYZData(XYZ xyz)
    {
        X = xyz.X;
        Y = xyz.Y;
        Z = xyz.Z;
    }

    public XYZ ToXYZ()
    {
        return new XYZ(X, Y, Z);
    }
}

public class TransformData
{
    [JsonProperty("origin")]
    public XYZData Origin { get; set; } = new();

    [JsonProperty("basisX")]
    public XYZData BasisX { get; set; } = new();

    [JsonProperty("basisY")]
    public XYZData BasisY { get; set; } = new();

    [JsonProperty("basisZ")]
    public XYZData BasisZ { get; set; } = new();

    public TransformData() { }

    public TransformData(Autodesk.Revit.DB.Transform transform)
    {
        Origin = new XYZData(transform.Origin);
        BasisX = new XYZData(transform.BasisX);
        BasisY = new XYZData(transform.BasisY);
        BasisZ = new XYZData(transform.BasisZ);
    }

    public Autodesk.Revit.DB.Transform ToTransform()
    {
        var transform = Autodesk.Revit.DB.Transform.CreateTranslation(Origin.ToXYZ());
        transform.BasisX = BasisX.ToXYZ();
        transform.BasisY = BasisY.ToXYZ();
        transform.BasisZ = BasisZ.ToXYZ();
        return transform;
    }
}

public class BoundingBoxData
{
    [JsonProperty("min")]
    public XYZData Min { get; set; } = new();

    [JsonProperty("max")]
    public XYZData Max { get; set; } = new();

    public BoundingBoxData() { }

    public BoundingBoxData(BoundingBoxXYZ bbox)
    {
        Min = new XYZData(bbox.Min);
        Max = new XYZData(bbox.Max);
    }
}

public class MeshGeometryData
{
    [JsonProperty("vertices")]
    public List<XYZData> Vertices { get; set; } = new();

    [JsonProperty("faces")]
    public List<int> Faces { get; set; } = new();

    [JsonProperty("normals")]
    public List<XYZData> Normals { get; set; } = new();
}

