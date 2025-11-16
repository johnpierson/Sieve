using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Newtonsoft.Json;

namespace PullRequestForRevit.Services;

public static class ParquetReader
{
    /// <summary>
    /// Gets list of all parquet files in a directory
    /// </summary>
    public static List<string> GetParquetFiles(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Logger.Instance.LogWarning($"Directory not found: {directoryPath}");
                return new List<string>();
            }

            var files = Directory.GetFiles(directoryPath, "*.parquet", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(f => f != null)
                .Cast<string>()
                .OrderBy(f => f)
                .ToList();

            Logger.Instance.LogInfo($"Found {files.Count} parquet files in {directoryPath}");
            return files;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error getting parquet files from {directoryPath}", ex);
            return new List<string>();
        }
    }

    /// <summary>
    /// Gets metadata about a parquet file
    /// </summary>
    public static async Task<ParquetFileInfo?> GetFileInfoAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Logger.Instance.LogWarning("GetFileInfo: File path is null or empty");
                return null;
            }

            if (!File.Exists(filePath))
            {
                Logger.Instance.LogWarning($"File not found: {filePath}");
                return null;
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                Logger.Instance.LogWarning($"File is empty: {filePath}");
                return null;
            }

            using var fileStream = File.OpenRead(filePath);
            using var parquetReader = await Parquet.ParquetReader.CreateAsync(fileStream);

            var schema = parquetReader.Schema;
            var rowCount = parquetReader.RowGroupCount > 0 
                ? parquetReader.OpenRowGroupReader(0).RowCount 
                : 0;

            // Get total row count by summing all row groups
            long totalRows = 0;
            for (int i = 0; i < parquetReader.RowGroupCount; i++)
            {
                using var rowGroup = parquetReader.OpenRowGroupReader(i);
                totalRows += rowGroup.RowCount;
            }

            var columns = schema.Fields.Select(f => new ParquetColumnInfo
            {
                Name = f.Name,
                DataType = f.GetType().Name.Replace("Field", ""),
                IsNullable = f.IsNullable
            }).ToList();

            return new ParquetFileInfo
            {
                FileName = Path.GetFileName(filePath),
                RowCount = totalRows,
                ColumnCount = columns.Count,
                Columns = columns,
                FileSize = fileInfo.Length,
                RowGroupCount = parquetReader.RowGroupCount
            };
        }
        catch (OutOfMemoryException ex)
        {
            Logger.Instance.LogError($"Out of memory reading parquet file: {filePath}", ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Instance.LogError($"Access denied reading parquet file: {filePath}", ex);
            return null;
        }
        catch (IOException ex)
        {
            Logger.Instance.LogError($"IO error reading parquet file: {filePath}", ex);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error getting file info for {filePath}", ex);
            return null;
        }
    }

    /// <summary>
    /// Reads paginated data from a parquet file
    /// </summary>
    public static async Task<ParquetDataResult?> ReadDataAsync(string filePath, int offset = 0, int limit = 100, string? sortColumn = null, bool ascending = true)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Logger.Instance.LogWarning("ReadData: File path is null or empty");
                return null;
            }

            if (!File.Exists(filePath))
            {
                Logger.Instance.LogWarning($"File not found: {filePath}");
                return null;
            }

            // Validate and limit parameters to prevent excessive memory usage
            offset = Math.Max(0, offset);
            limit = Math.Min(Math.Max(1, limit), 1000); // Cap at 1000 rows per request

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                Logger.Instance.LogWarning($"File is empty: {filePath}");
                return new ParquetDataResult
                {
                    Rows = new List<Dictionary<string, object?>>(),
                    Columns = new List<string>(),
                    TotalRows = 0,
                    Offset = offset,
                    Limit = limit
                };
            }

            using var fileStream = File.OpenRead(filePath);
            using var parquetReader = await Parquet.ParquetReader.CreateAsync(fileStream);

            var schema = parquetReader.Schema;
            var columns = schema.Fields.Select(f => f.Name).ToList();

            // Read all data (for small files) or paginated (for large files)
            var allRows = new List<Dictionary<string, object?>>();
            long totalRows = 0;

            for (int rg = 0; rg < parquetReader.RowGroupCount; rg++)
            {
                using var rowGroup = parquetReader.OpenRowGroupReader(rg);
                totalRows += rowGroup.RowCount;

                // Read all fields
                var dataFields = schema.Fields.OfType<DataField>().ToArray();
                var dataColumns = new DataColumn[dataFields.Length];

                for (int i = 0; i < dataFields.Length; i++)
                {
                    dataColumns[i] = await rowGroup.ReadColumnAsync(dataFields[i]);
                }

                // Convert to dictionaries
                for (int row = 0; row < rowGroup.RowCount; row++)
                {
                    var rowDict = new Dictionary<string, object?>();
                    for (int col = 0; col < dataFields.Length; col++)
                    {
                        var field = dataFields[col];
                        var dataColumn = dataColumns[col];
                        var value = GetValue(dataColumn, row);
                        rowDict[field.Name] = value;
                    }
                    allRows.Add(rowDict);
                }
            }

            // Apply sorting if requested
            if (!string.IsNullOrEmpty(sortColumn) && columns.Contains(sortColumn))
            {
                allRows = ascending
                    ? allRows.OrderBy(r => r.ContainsKey(sortColumn) ? r[sortColumn] : null).ToList()
                    : allRows.OrderByDescending(r => r.ContainsKey(sortColumn) ? r[sortColumn] : null).ToList();
            }

            // Apply pagination
            var paginatedRows = allRows.Skip(offset).Take(limit).ToList();

            return new ParquetDataResult
            {
                Rows = paginatedRows,
                Columns = columns,
                TotalRows = totalRows,
                Offset = offset,
                Limit = limit
            };
        }
        catch (OutOfMemoryException ex)
        {
            Logger.Instance.LogError($"Out of memory reading parquet file: {filePath}", ex);
            return new ParquetDataResult
            {
                Rows = new List<Dictionary<string, object?>>(),
                Columns = new List<string>(),
                TotalRows = 0,
                Offset = offset,
                Limit = limit
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Instance.LogError($"Access denied reading parquet file: {filePath}", ex);
            return null;
        }
        catch (IOException ex)
        {
            Logger.Instance.LogError($"IO error reading parquet file: {filePath}", ex);
            return null;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError($"Error reading data from {filePath}", ex);
            return new ParquetDataResult
            {
                Rows = new List<Dictionary<string, object?>>(),
                Columns = new List<string>(),
                TotalRows = 0,
                Offset = offset,
                Limit = limit
            };
        }
    }

    private static object? GetValue(DataColumn column, int rowIndex)
    {
        try
        {
            if (rowIndex >= column.Data.Length)
                return null;

            var value = column.Data.GetValue(rowIndex);
            
            // Convert to JSON-serializable types
            if (value == null || value == DBNull.Value)
                return null;

            // Handle DateTime
            if (value is DateTime dt)
                return dt.ToString("O"); // ISO 8601 format

            // Handle arrays/lists
            if (value is System.Array arr)
            {
                return arr.Cast<object>().Select(v => v?.ToString()).ToArray();
            }

            return value;
        }
        catch (Exception ex)
        {
            Logger.Instance.LogDebug($"Error getting value at row {rowIndex}: {ex.Message}");
            return null;
        }
    }
}

public class ParquetFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<ParquetColumnInfo> Columns { get; set; } = new();
    public long FileSize { get; set; }
    public int RowGroupCount { get; set; }
}

public class ParquetColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
}

public class ParquetDataResult
{
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public long TotalRows { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
}

