using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Sieve
{
    /// <summary>
    /// Fast parser for .parquet files with optimized reading capabilities
    /// </summary>
    public class ParquetParser : IDisposable
    {
        private readonly ParquetReader? _reader;
        private readonly Stream? _stream;
        private bool _disposed;

        /// <summary>
        /// Gets the schema of the parquet file
        /// </summary>
        public Schema? Schema => _reader?.Schema;

        /// <summary>
        /// Gets the number of row groups in the file
        /// </summary>
        public int RowGroupCount => _reader?.RowGroupCount ?? 0;

        /// <summary>
        /// Initializes a new instance of the ParquetParser from a file path
        /// </summary>
        /// <param name="filePath">Path to the parquet file</param>
        public ParquetParser(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Parquet file not found: {filePath}");

            _stream = File.OpenRead(filePath);
            _reader = new ParquetReader(_stream);
        }

        /// <summary>
        /// Initializes a new instance of the ParquetParser from a stream
        /// </summary>
        /// <param name="stream">Stream containing parquet data</param>
        public ParquetParser(Stream stream)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _reader = new ParquetReader(_stream);
        }

        /// <summary>
        /// Reads all data from the parquet file as a DataTable
        /// </summary>
        /// <param name="columnNames">Optional list of column names to read. If null, all columns are read.</param>
        /// <returns>DataTable containing all rows</returns>
        public DataTable ReadAsDataTable(IEnumerable<string>? columnNames = null)
        {
            ThrowIfDisposed();

            var dataTable = new DataTable();
            var columnsToRead = GetColumnsToRead(columnNames);

            // Build DataTable schema
            foreach (var field in _reader!.Schema.Fields)
            {
                if (columnsToRead == null || columnsToRead.Contains(field.Name))
                {
                    var column = new DataColumn(field.Name, GetClrType(field));
                    dataTable.Columns.Add(column);
                }
            }

            // Read all row groups
            for (int i = 0; i < _reader.RowGroupCount; i++)
            {
                using var rowGroupReader = _reader.OpenRowGroupReader(i);
                var rows = ReadRowGroup(rowGroupReader, columnsToRead);
                
                foreach (var row in rows)
                {
                    dataTable.Rows.Add(row);
                }
            }

            return dataTable;
        }

        /// <summary>
        /// Reads all data from the parquet file as a list of dictionaries
        /// </summary>
        /// <param name="columnNames">Optional list of column names to read. If null, all columns are read.</param>
        /// <returns>List of dictionaries where each dictionary represents a row</returns>
        public List<Dictionary<string, object?>> ReadAsDictionaries(IEnumerable<string>? columnNames = null)
        {
            ThrowIfDisposed();

            var result = new List<Dictionary<string, object?>>();
            var columnsToRead = GetColumnsToRead(columnNames);

            for (int i = 0; i < _reader!.RowGroupCount; i++)
            {
                using var rowGroupReader = _reader.OpenRowGroupReader(i);
                var rows = ReadRowGroupAsDictionaries(rowGroupReader, columnsToRead);
                result.AddRange(rows);
            }

            return result;
        }

        /// <summary>
        /// Reads all data from the parquet file as strongly-typed objects
        /// </summary>
        /// <typeparam name="T">Type to deserialize to</typeparam>
        /// <param name="columnNames">Optional list of column names to read. If null, all columns are read.</param>
        /// <returns>List of strongly-typed objects</returns>
        public List<T> ReadAs<T>(IEnumerable<string>? columnNames = null) where T : class, new()
        {
            ThrowIfDisposed();

            var dictionaries = ReadAsDictionaries(columnNames);
            var result = new List<T>();

            foreach (var dict in dictionaries)
            {
                var obj = new T();
                var properties = typeof(T).GetProperties();

                foreach (var prop in properties)
                {
                    if (dict.TryGetValue(prop.Name, out var value) && value != null)
                    {
                        var convertedValue = ConvertValue(value, prop.PropertyType);
                        prop.SetValue(obj, convertedValue);
                    }
                }

                result.Add(obj);
            }

            return result;
        }

        /// <summary>
        /// Reads a specific row group as a DataTable
        /// </summary>
        /// <param name="rowGroupIndex">Index of the row group to read</param>
        /// <param name="columnNames">Optional list of column names to read</param>
        /// <returns>DataTable containing rows from the specified row group</returns>
        public DataTable ReadRowGroupAsDataTable(int rowGroupIndex, IEnumerable<string>? columnNames = null)
        {
            ThrowIfDisposed();

            if (rowGroupIndex < 0 || rowGroupIndex >= _reader!.RowGroupCount)
                throw new ArgumentOutOfRangeException(nameof(rowGroupIndex));

            var dataTable = new DataTable();
            var columnsToRead = GetColumnsToRead(columnNames);

            // Build DataTable schema
            foreach (var field in _reader.Schema.Fields)
            {
                if (columnsToRead == null || columnsToRead.Contains(field.Name))
                {
                    var column = new DataColumn(field.Name, GetClrType(field));
                    dataTable.Columns.Add(column);
                }
            }

            using var rowGroupReader = _reader.OpenRowGroupReader(rowGroupIndex);
            var rows = ReadRowGroup(rowGroupReader, columnsToRead);

            foreach (var row in rows)
            {
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        /// <summary>
        /// Reads data in parallel from multiple row groups (faster for large files)
        /// </summary>
        /// <param name="columnNames">Optional list of column names to read</param>
        /// <param name="maxDegreeOfParallelism">Maximum number of concurrent row group reads</param>
        /// <returns>List of dictionaries containing all rows</returns>
        public List<Dictionary<string, object?>> ReadParallel(IEnumerable<string>? columnNames = null, int maxDegreeOfParallelism = -1)
        {
            ThrowIfDisposed();

            var result = new ConcurrentBag<Dictionary<string, object?>>();
            var columnsToRead = GetColumnsToRead(columnNames);

            Parallel.For(0, _reader!.RowGroupCount, new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            }, rowGroupIndex =>
            {
                using var rowGroupReader = _reader.OpenRowGroupReader(rowGroupIndex);
                var rows = ReadRowGroupAsDictionaries(rowGroupReader, columnsToRead);
                
                foreach (var row in rows)
                {
                    result.Add(row);
                }
            });

            return result.ToList();
        }

        /// <summary>
        /// Gets metadata about the parquet file
        /// </summary>
        /// <returns>Dictionary containing file metadata</returns>
        public Dictionary<string, string> GetMetadata()
        {
            ThrowIfDisposed();

            var metadata = new Dictionary<string, string>
            {
                ["RowGroupCount"] = _reader!.RowGroupCount.ToString(),
                ["FieldCount"] = _reader.Schema.Fields.Count.ToString()
            };

            // Add field information
            foreach (var field in _reader.Schema.Fields)
            {
                metadata[$"Field_{field.Name}"] = $"{field.Name} ({field.DataType})";
            }

            return metadata;
        }

        /// <summary>
        /// Gets the column names in the parquet file
        /// </summary>
        /// <returns>List of column names</returns>
        public List<string> GetColumnNames()
        {
            ThrowIfDisposed();
            return _reader!.Schema.Fields.Select(f => f.Name).ToList();
        }

        /// <summary>
        /// Reads all data from the parquet file as a DataTable asynchronously
        /// </summary>
        /// <param name="columnNames">Optional list of column names to read. If null, all columns are read.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>DataTable containing all rows</returns>
        public async Task<DataTable> ReadAsDataTableAsync(IEnumerable<string>? columnNames = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => ReadAsDataTable(columnNames), cancellationToken);
        }

        /// <summary>
        /// Reads all data from the parquet file as a list of dictionaries asynchronously
        /// </summary>
        /// <param name="columnNames">Optional list of column names to read. If null, all columns are read.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of dictionaries where each dictionary represents a row</returns>
        public async Task<List<Dictionary<string, object?>>> ReadAsDictionariesAsync(IEnumerable<string>? columnNames = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => ReadAsDictionaries(columnNames), cancellationToken);
        }

        /// <summary>
        /// Reads all data from the parquet file as strongly-typed objects asynchronously
        /// </summary>
        /// <typeparam name="T">Type to deserialize to</typeparam>
        /// <param name="columnNames">Optional list of column names to read. If null, all columns are read.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of strongly-typed objects</returns>
        public async Task<List<T>> ReadAsAsync<T>(IEnumerable<string>? columnNames = null, CancellationToken cancellationToken = default) where T : class, new()
        {
            return await Task.Run(() => ReadAs<T>(columnNames), cancellationToken);
        }

        /// <summary>
        /// Reads data in parallel from multiple row groups asynchronously (faster for large files)
        /// </summary>
        /// <param name="columnNames">Optional list of column names to read</param>
        /// <param name="maxDegreeOfParallelism">Maximum number of concurrent row group reads</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of dictionaries containing all rows</returns>
        public async Task<List<Dictionary<string, object?>>> ReadParallelAsync(IEnumerable<string>? columnNames = null, int maxDegreeOfParallelism = -1, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => ReadParallel(columnNames, maxDegreeOfParallelism), cancellationToken);
        }

        private HashSet<string>? GetColumnsToRead(IEnumerable<string>? columnNames)
        {
            if (columnNames == null)
                return null;

            var columnSet = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);
            var availableColumns = _reader!.Schema.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Validate that all requested columns exist
            var missingColumns = columnSet.Except(availableColumns).ToList();
            if (missingColumns.Any())
            {
                throw new ArgumentException($"The following columns were not found in the parquet file: {string.Join(", ", missingColumns)}");
            }

            return columnSet;
        }

        private List<object?[]> ReadRowGroup(RowGroupReader rowGroupReader, HashSet<string>? columnsToRead)
        {
            var rows = new List<object?[]>();
            var fieldsToRead = _reader!.Schema.Fields
                .Where(f => columnsToRead == null || columnsToRead.Contains(f.Name))
                .ToList();

            if (!fieldsToRead.Any())
                return rows;

            // Read all columns for the row group
            var dataColumns = new DataColumn[fieldsToRead.Count];
            for (int i = 0; i < fieldsToRead.Count; i++)
            {
                var field = fieldsToRead[i];
                var dataColumn = rowGroupReader.ReadColumn(field);
                dataColumns[i] = dataColumn;
            }

            // Determine row count from first column
            var rowCount = dataColumns[0].Data.Length;

            // Build rows
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var row = new object?[fieldsToRead.Count];
                for (int colIndex = 0; colIndex < fieldsToRead.Count; colIndex++)
                {
                    var dataColumn = dataColumns[colIndex];
                    row[colIndex] = GetValueFromDataColumn(dataColumn, rowIndex);
                }
                rows.Add(row);
            }

            return rows;
        }

        private List<Dictionary<string, object?>> ReadRowGroupAsDictionaries(RowGroupReader rowGroupReader, HashSet<string>? columnsToRead)
        {
            var rows = new List<Dictionary<string, object?>>();
            var fieldsToRead = _reader!.Schema.Fields
                .Where(f => columnsToRead == null || columnsToRead.Contains(f.Name))
                .ToList();

            if (!fieldsToRead.Any())
                return rows;

            // Read all columns for the row group
            var dataColumns = new Dictionary<string, DataColumn>();
            foreach (var field in fieldsToRead)
            {
                var dataColumn = rowGroupReader.ReadColumn(field);
                dataColumns[field.Name] = dataColumn;
            }

            // Determine row count from first column
            var rowCount = dataColumns.Values.First().Data.Length;

            // Build rows as dictionaries
            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                var row = new Dictionary<string, object?>();
                foreach (var field in fieldsToRead)
                {
                    var dataColumn = dataColumns[field.Name];
                    row[field.Name] = GetValueFromDataColumn(dataColumn, rowIndex);
                }
                rows.Add(row);
            }

            return rows;
        }

        private object? GetValueFromDataColumn(DataColumn dataColumn, int rowIndex)
        {
            var array = dataColumn.Data;
            if (rowIndex >= array.Length)
                return null;

            return array.GetValue(rowIndex);
        }

        private Type GetClrType(Field field)
        {
            return field.DataType switch
            {
                DataType.Boolean => typeof(bool),
                DataType.Int32 => typeof(int),
                DataType.Int64 => typeof(long),
                DataType.Float => typeof(float),
                DataType.Double => typeof(double),
                DataType.String => typeof(string),
                DataType.ByteArray => typeof(byte[]),
                DataType.DateTimeOffset => typeof(DateTimeOffset),
                DataType.Decimal => typeof(decimal),
                DataType.Int96 => typeof(DateTime),
                _ => typeof(object)
            };
        }

        private object? ConvertValue(object? value, Type targetType)
        {
            if (value == null || value == DBNull.Value)
                return null;

            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // Special handling for common conversions
            if (value is DateTimeOffset dto && underlyingType == typeof(DateTime))
                return dto.DateTime;

            if (value is DateTime dt && underlyingType == typeof(DateTimeOffset))
                return new DateTimeOffset(dt);

            try
            {
                return Convert.ChangeType(value, underlyingType);
            }
            catch
            {
                return value;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ParquetParser));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _reader?.Dispose();
                _stream?.Dispose();
                _disposed = true;
            }
        }
    }
}

