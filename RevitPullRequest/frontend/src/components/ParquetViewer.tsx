import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { FileText, ChevronLeft, ChevronRight, ArrowUpDown } from "lucide-react";
import type {
  ParquetFileInfo,
  ParquetFilesResponse,
  ParquetFileInfoResponse,
  ParquetDataResponse,
  IncomingMessage,
  OutgoingMessage,
} from "@/types/messages";

export default function ParquetViewer() {
  const [files, setFiles] = useState<string[]>([]);
  const [selectedFile, setSelectedFile] = useState<string>("");
  const [fileInfo, setFileInfo] = useState<ParquetFileInfo | null>(null);
  const [data, setData] = useState<ParquetDataResponse | null>(null);
  const [currentPage, setCurrentPage] = useState(1);
  const [sortColumn, setSortColumn] = useState<string | null>(null);
  const [sortAscending, setSortAscending] = useState(true);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const rowsPerPage = 100;

  useEffect(() => {
    const handleMessage = (event: Event & { data?: any }) => {
      try {
        let messageData = event.data;
        if (typeof messageData === "string") {
          messageData = JSON.parse(messageData);
        }

        console.log("ParquetViewer received message:", messageData);
        const message = messageData as IncomingMessage;

        switch (message.type) {
          case "parquet:files:response":
            const filesResponse = message as ParquetFilesResponse;
            if (filesResponse.error) {
              setError(filesResponse.error);
            } else {
              setFiles(filesResponse.files);
            }
            break;

          case "parquet:info:response":
            const infoResponse = message as ParquetFileInfoResponse;
            if (infoResponse.error) {
              setError(infoResponse.error);
              setFileInfo(null);
            } else if (infoResponse.info) {
              setFileInfo(infoResponse.info);
              setError(null);
            } else {
              setError("Invalid file info response");
              setFileInfo(null);
            }
            break;

          case "parquet:data:response":
            const dataResponse = message as ParquetDataResponse;
            setLoading(false);
            if (dataResponse.error) {
              setError(dataResponse.error);
            } else {
              setData(dataResponse);
              setError(null);
            }
            break;
        }
      } catch (error) {
        console.error("Error parsing message:", error);
      }
    };

    if (window.chrome?.webview) {
      window.chrome.webview.addEventListener("message", handleMessage);
    }
    window.addEventListener("message", handleMessage as EventListener);

    // Load file list on mount
    sendMessage({ type: "parquet:get:files" });

    return () => {
      if (window.chrome?.webview) {
        window.chrome.webview.removeEventListener("message", handleMessage);
      }
      window.removeEventListener("message", handleMessage as EventListener);
    };
  }, []);

  const sendMessage = (message: OutgoingMessage) => {
    try {
      if (window.chrome?.webview) {
        window.chrome.webview.postMessage(message);
      } else {
        console.warn("WebView not available:", message);
      }
    } catch (error) {
      console.error("Error sending message:", error);
    }
  };

  const handleFileSelect = (fileName: string) => {
    setSelectedFile(fileName);
    setFileInfo(null);
    setData(null);
    setCurrentPage(1);
    setSortColumn(null);
    setError(null);

    // Load file info
    sendMessage({
      type: "parquet:get:info",
      fileName: fileName,
    });
  };

  const loadData = (page: number = 1) => {
    if (!selectedFile) return;

    setLoading(true);
    setError(null);
    const offset = (page - 1) * rowsPerPage;

    sendMessage({
      type: "parquet:get:data",
      fileName: selectedFile,
      offset: offset,
      limit: rowsPerPage,
      sortColumn: sortColumn || undefined,
      ascending: sortAscending,
    });
  };

  useEffect(() => {
    if (fileInfo && selectedFile) {
      loadData(currentPage);
    }
  }, [fileInfo, currentPage, sortColumn, sortAscending]);

  const handleSort = (column: string) => {
    if (sortColumn === column) {
      setSortAscending(!sortAscending);
    } else {
      setSortColumn(column);
      setSortAscending(true);
    }
    setCurrentPage(1);
  };

  const formatBytes = (bytes: number): string => {
    if (!bytes || bytes === 0) return "0 Bytes";
    const k = 1024;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + " " + sizes[i];
  };

  const formatValue = (value: any): string => {
    if (value === null || value === undefined) {
      return "<em class='text-gray-400'>null</em>";
    }
    if (typeof value === "object") {
      return escapeHtml(JSON.stringify(value));
    }
    return escapeHtml(String(value));
  };

  const escapeHtml = (text: string): string => {
    const div = document.createElement("div");
    div.textContent = text;
    return div.innerHTML;
  };

  const totalPages = data && data.totalRows
    ? Math.ceil(data.totalRows / rowsPerPage)
    : 0;

  return (
    <div className="flex flex-col h-full p-4 gap-4">
      <div className="flex items-center gap-3">
        <FileText className="w-6 h-6" />
        <h1 className="text-2xl font-bold">Parquet Viewer</h1>
      </div>

      {/* File Selector */}
      <div className="flex gap-2 items-center">
        <select
          value={selectedFile}
          onChange={(e) => handleFileSelect(e.target.value)}
          className="flex-1 px-3 py-2 border rounded-md bg-white"
        >
          <option value="">Select a parquet file...</option>
          {files.map((file) => (
            <option key={file} value={file}>
              {file}
            </option>
          ))}
        </select>
      </div>

      {/* Error Display */}
      {error && (
        <div className="p-3 bg-red-100 dark:bg-red-900 rounded text-sm text-red-800 dark:text-red-200">
          {error}
        </div>
      )}

      {/* File Info */}
      {fileInfo && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 p-4 bg-gray-50 dark:bg-gray-800 rounded-md">
          <div>
            <div className="text-xs text-gray-500 uppercase font-semibold">
              Total Rows
            </div>
            <div className="text-xl font-bold">
              {(fileInfo.rowCount ?? 0).toLocaleString()}
            </div>
          </div>
          <div>
            <div className="text-xs text-gray-500 uppercase font-semibold">
              Columns
            </div>
            <div className="text-xl font-bold">{fileInfo.columnCount ?? 0}</div>
          </div>
          <div>
            <div className="text-xs text-gray-500 uppercase font-semibold">
              File Size
            </div>
            <div className="text-xl font-bold">
              {formatBytes(fileInfo.fileSize ?? 0)}
            </div>
          </div>
          <div>
            <div className="text-xs text-gray-500 uppercase font-semibold">
              Row Groups
            </div>
            <div className="text-xl font-bold">{fileInfo.rowGroupCount ?? 0}</div>
          </div>
        </div>
      )}

      {/* Columns Info */}
      {fileInfo && fileInfo.columns && fileInfo.columns.length > 0 && (
        <div className="p-3 bg-gray-50 dark:bg-gray-800 rounded-md">
          <div className="text-sm font-semibold mb-2">Columns:</div>
          <div className="flex flex-wrap gap-2">
            {fileInfo.columns.map((col) => (
              <div
                key={col.name}
                className="px-2 py-1 bg-white dark:bg-gray-700 rounded text-xs"
              >
                <span>{col.name}</span>
                <span className="ml-2 text-blue-600 dark:text-blue-400">
                  {col.dataType}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Data Table */}
      {data && data.rows.length > 0 && (
        <div className="flex-1 overflow-auto border rounded-md">
          <div className="overflow-x-auto">
            <table className="w-full border-collapse">
              <thead className="bg-blue-600 text-white sticky top-0">
                <tr>
                  {data.columns.map((col) => (
                    <th
                      key={col}
                      className="px-4 py-2 text-left cursor-pointer hover:bg-blue-700 select-none border-r border-blue-500 last:border-r-0"
                      onClick={() => handleSort(col)}
                    >
                      <div className="flex items-center gap-2">
                        {col}
                        {sortColumn === col && (
                          <ArrowUpDown
                            size={14}
                            className={sortAscending ? "rotate-180" : ""}
                          />
                        )}
                      </div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data.rows.map((row, idx) => (
                  <tr
                    key={idx}
                    className="border-b hover:bg-gray-50 dark:hover:bg-gray-800 even:bg-gray-50 dark:even:bg-gray-900"
                  >
                    {data.columns.map((col) => (
                      <td
                        key={col}
                        className="px-4 py-2 text-sm border-r last:border-r-0"
                        dangerouslySetInnerHTML={{
                          __html: formatValue(row[col]),
                        }}
                      />
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* Loading State */}
      {loading && (
        <div className="text-center py-8 text-gray-500">Loading data...</div>
      )}

      {/* Empty State */}
      {!loading && !data && selectedFile && (
        <div className="text-center py-8 text-gray-500">
          No data to display
        </div>
      )}

      {/* Pagination */}
      {data && totalPages > 1 && (
        <div className="flex justify-between items-center p-4 border-t">
          <div className="text-sm text-gray-600 dark:text-gray-400">
            Showing {(data.offset ?? 0) + 1}-
            {Math.min((data.offset ?? 0) + (data.limit ?? 0), data.totalRows ?? 0)} of{" "}
            {(data.totalRows ?? 0).toLocaleString()} rows
          </div>
          <div className="flex gap-2 items-center">
            <Button
              onClick={() => {
                if (currentPage > 1) {
                  setCurrentPage(currentPage - 1);
                }
              }}
              disabled={currentPage === 1}
              variant="outline"
              size="sm"
            >
              <ChevronLeft size={16} className="mr-1" />
              Previous
            </Button>
            <span className="px-4 text-sm">
              Page {currentPage} of {totalPages}
            </span>
            <Button
              onClick={() => {
                if (currentPage < totalPages) {
                  setCurrentPage(currentPage + 1);
                }
              }}
              disabled={currentPage >= totalPages}
              variant="outline"
              size="sm"
            >
              Next
              <ChevronRight size={16} className="ml-1" />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

