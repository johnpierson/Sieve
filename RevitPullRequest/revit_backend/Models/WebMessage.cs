using System.Collections.Generic;
using Newtonsoft.Json;
using PullRequestForRevit.Services;

namespace PullRequestForRevit.Models;

[JsonObject]
public class WebMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = string.Empty;

    [JsonProperty("content")]
    public object? Content { get; set; }
}

[JsonObject]
public class RecordMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "record:elements";

    [JsonProperty("elementIds")]
    public List<string>? ElementIds { get; set; }
}

[JsonObject]
public class CompareMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "compare:elements";

    [JsonProperty("elementIds")]
    public List<string>? ElementIds { get; set; }
}

[JsonObject]
public class ClearVisualizationMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "clear:visualization";
}

[JsonObject]
public class GetSelectionMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "get:selection";
}

[JsonObject]
public class SelectionChangedMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "selection:changed";

    [JsonProperty("elements")]
    public List<ElementInfo> Elements { get; set; } = new();
}

[JsonObject]
public class ElementInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;
}

[JsonObject]
public class RecordCompleteMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "record:complete";

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("results")]
    public List<RecordResult> Results { get; set; } = new();

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class RecordResult
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class CompareCompleteMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "compare:complete";

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("results")]
    public List<CompareResult> Results { get; set; } = new();

    // Indicates whether Revit DirectContext3D visualization was successfully created.
    // Comparison results are still valid even if this is false.
    [JsonProperty("visualizationCreated")]
    public bool VisualizationCreated { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class CompareResult
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("category")]
    public string Category { get; set; } = string.Empty;

    [JsonProperty("hasChanges")]
    public bool HasChanges { get; set; }

    [JsonProperty("changeType")]
    public string ChangeType { get; set; } = "none"; // "none", "moved", "parameter", "deleted", "multiple"

    [JsonProperty("translation")]
    public XYZData? Translation { get; set; }

    [JsonProperty("parameterChanges")]
    public List<ParameterChange> ParameterChanges { get; set; } = new();

    [JsonProperty("isDeleted")]
    public bool IsDeleted { get; set; }

    // Bounding boxes for web visualization (before / after)
    [JsonProperty("recordedBoundingBox")]
    public BoundingBoxData? RecordedBoundingBox { get; set; }

    [JsonProperty("currentBoundingBox")]
    public BoundingBoxData? CurrentBoundingBox { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class ParameterChange
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("oldValue")]
    public object? OldValue { get; set; }

    [JsonProperty("newValue")]
    public object? NewValue { get; set; }
}

[JsonObject]
public class GetParquetFilesMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "parquet:get:files";
}

[JsonObject]
public class GetParquetFileInfoMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "parquet:get:info";

    [JsonProperty("fileName")]
    public string FileName { get; set; } = string.Empty;
}

[JsonObject]
public class GetParquetDataMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "parquet:get:data";

    [JsonProperty("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonProperty("offset")]
    public int Offset { get; set; } = 0;

    [JsonProperty("limit")]
    public int Limit { get; set; } = 100;

    [JsonProperty("sortColumn")]
    public string? SortColumn { get; set; }

    [JsonProperty("ascending")]
    public bool Ascending { get; set; } = true;
}

[JsonObject]
public class ParquetFilesResponse
{
    [JsonProperty("type")]
    public string Type { get; set; } = "parquet:files:response";

    [JsonProperty("files")]
    public List<string> Files { get; set; } = new();

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class ParquetFileInfoResponse
{
    [JsonProperty("type")]
    public string Type { get; set; } = "parquet:info:response";

    [JsonProperty("info")]
    public ParquetFileInfo? Info { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class ParquetDataResponse
{
    [JsonProperty("type")]
    public string Type { get; set; } = "parquet:data:response";

    [JsonProperty("rows")]
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    [JsonProperty("columns")]
    public List<string> Columns { get; set; } = new();

    [JsonProperty("totalRows")]
    public long TotalRows { get; set; }

    [JsonProperty("offset")]
    public int Offset { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("error")]
    public string? Error { get; set; }
}

[JsonObject]
public class NotificationMessage
{
    [JsonProperty("type")]
    public string Type { get; set; } = "notification";

    // "info", "success", "warning", or "error"
    [JsonProperty("level")]
    public string Level { get; set; } = "info";

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}

