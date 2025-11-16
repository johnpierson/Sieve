import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { CircleDot, GitCompare, Trash2, Circle, FileText, Database } from "lucide-react";
import logo from "../../asset/logo.png";
import type {
  ElementInfo,
  IncomingMessage,
  OutgoingMessage,
  CompareResult,
  NotificationMessage,
} from "@/types/messages";
import ParquetViewer from "./ParquetViewer";
import WebComparisonViewer from "./WebComparisonViewer";

type ViewMode = "main" | "parquet";

export default function PullRequestForRevitApp() {
  const [viewMode, setViewMode] = useState<ViewMode>("main");
  const [role, setRole] = useState<"Designer" | "PM">("Designer");
  const [selectedElements, setSelectedElements] = useState<ElementInfo[]>([]);
  const [isRecording, setIsRecording] = useState(false);
  const [isComparing, setIsComparing] = useState(false);
  const [recordStatus, setRecordStatus] = useState<string | null>(null);
  const [compareStatus, setCompareStatus] = useState<string | null>(null);
  const [compareResults, setCompareResults] = useState<CompareResult[] | null>(null);
  const [visualizationCreated, setVisualizationCreated] = useState<boolean | null>(null);
  const [compareFilter, setCompareFilter] = useState<string>("");
  const [confirmedIds, setConfirmedIds] = useState<string[]>([]);
  const [submittedIds, setSubmittedIds] = useState<string[]>([]);
  const [approvedIds, setApprovedIds] = useState<string[]>([]);
  const [rejectedIds, setRejectedIds] = useState<string[]>([]);
  const [notifications, setNotifications] = useState<
    { id: number; message: string; level: "info" | "success" | "warning" | "error" }[]
  >([]);
  const nextNotificationIdRef = useRef(1);

  const hasAnyCompareChanges =
    compareResults && compareResults.some((r) => r.hasChanges);

  // Listen for messages from Revit backend
  useEffect(() => {
    const handleMessage = (event: Event & { data?: any }) => {
      try {
        // WebView2 sends JSON strings via PostWebMessageAsJson
        // event.data is the JSON string that needs to be parsed
        let data = event.data;
        
        // If data is already an object, use it directly (browser testing)
        // Otherwise, parse it as JSON string (WebView2)
        if (typeof data === "string") {
          data = JSON.parse(data);
        }
        
        const message: IncomingMessage = data as IncomingMessage;
        console.log("Received message from Revit:", message);

        if (!message || !message.type) {
          console.warn("Invalid message format:", message);
          return;
        }

        switch (message.type) {
          case "selection:changed":
            setSelectedElements(message.elements);
            break;

          case "record:complete":
            setIsRecording(false);
            if (message.success) {
              const successCount = message.results.filter((r) => r.success).length;
              setRecordStatus(
                `Recorded ${successCount}/${message.results.length} elements successfully`
              );
            } else {
              setRecordStatus(
                message.error || "Recording failed"
              );
            }
            // Clear status after 3 seconds
            setTimeout(() => setRecordStatus(null), 3000);
            break;

          case "compare:complete": {
            setIsComparing(false);
            setCompareResults(message.results);
            setVisualizationCreated(
              typeof message.visualizationCreated === "boolean"
                ? message.visualizationCreated
                : null
            );
        // Reset confirmations/approvals on new compare run
        setConfirmedIds([]);
        setSubmittedIds([]);
        setApprovedIds([]);
        setRejectedIds([]);

            const changedCount = message.results.filter((r) => r.hasChanges).length;
            if (message.success) {
              setCompareStatus(
                `Found changes in ${changedCount}/${message.results.length} elements`
              );
            } else {
              setCompareStatus(
                message.error || "Comparison failed"
              );
            }
            // Clear status text after 3 seconds, but keep results visible
            setTimeout(() => setCompareStatus(null), 3000);
            break;
          }

          case "notification": {
            const n = message as NotificationMessage;
            const id = nextNotificationIdRef.current++;
            setNotifications((prev) => [...prev, { id, message: n.message, level: n.level }]);
            setTimeout(() => {
              setNotifications((prev) => prev.filter((m) => m.id !== id));
            }, 4000);
            break;
          }
        }
      } catch (error) {
        console.error("Error parsing message from Revit:", error, event);
      }
    };

    // Use WebView2 messaging pattern
    if (window.chrome?.webview) {
      window.chrome.webview.addEventListener("message", handleMessage);
    }

    // Also listen for standard postMessage (for browser testing)
    window.addEventListener("message", handleMessage as EventListener);

    // Request initial selection
    sendMessage({ type: "get:selection" });

    return () => {
      if (window.chrome?.webview) {
        window.chrome.webview.removeEventListener("message", handleMessage);
      }
      window.removeEventListener("message", handleMessage as EventListener);
    };
  }, []);

  const sendMessage = (message: OutgoingMessage) => {
    try {
      console.log("Sending message to Revit:", message);

      if (window.chrome?.webview) {
        // WebView2 postMessage automatically serializes to JSON
        window.chrome.webview.postMessage(message);
      } else {
        // Fallback for browser testing
        console.warn("WebView not available, message not sent:", message);
      }
    } catch (error) {
      console.error("Error sending message to Revit:", error);
    }
  };

  const handleRecord = () => {
    if (selectedElements.length === 0) {
      setRecordStatus("No elements selected");
      setTimeout(() => setRecordStatus(null), 3000);
      return;
    }

    setIsRecording(true);
    setRecordStatus(null);
    const elementIds = selectedElements.map((el) => el.id);
    sendMessage({
      type: "record:elements",
      elementIds,
    });
  };

  const handleCompare = () => {
    // Backend supports comparing ALL recorded elements when no elementIds are provided.
    // If there is a selection, we compare only those; if not, we compare all recorded
    // elements from the current session folder.
    setIsComparing(true);

    if (selectedElements.length === 0) {
      setCompareStatus("Comparing all recorded elements in this session");
      sendMessage({
        type: "compare:elements",
        // No elementIds => backend will call LoadAllRecordedData(_dumpFolder)
      } as any);
    } else {
    setCompareStatus(null);
    const elementIds = selectedElements.map((el) => el.id);
    sendMessage({
      type: "compare:elements",
      elementIds,
    });
    }
  };

  const handleClearVisualization = () => {
    sendMessage({ type: "clear:visualization" });
    setCompareStatus("Visualization cleared");
    setTimeout(() => setCompareStatus(null), 3000);
  };

  if (viewMode === "parquet") {
    return (
      <div className="flex flex-col h-screen">
        <div className="flex border-b">
          <button
            onClick={() => setViewMode("main")}
            className="px-4 py-2 hover:bg-gray-100 dark:hover:bg-gray-800"
          >
            <Database className="w-4 h-4 inline mr-2" />
            Main
          </button>
          <button
            onClick={() => setViewMode("parquet")}
            className="px-4 py-2 bg-blue-100 dark:bg-blue-900 hover:bg-blue-200 dark:hover:bg-blue-800"
          >
            <FileText className="w-4 h-4 inline mr-2" />
            Parquet Viewer
          </button>
        </div>
        <div className="flex-1 overflow-auto">
          <ParquetViewer />
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-screen">
      <div className="flex items-center justify-between border-b px-2">
        <div className="flex items-center gap-2">
          <img
            src={logo}
            alt="PullRequest-For-Revit logo"
            className="h-12 w-12 object-contain"
          />
          <button
            onClick={() => setViewMode("main")}
            className="px-4 py-2 bg-blue-100 dark:bg-blue-900 hover:bg-blue-200 dark:hover:bg-blue-800"
          >
            <Database className="w-4 h-4 inline mr-2" />
            Main
          </button>
          <button
            onClick={() => setViewMode("parquet")}
            className="px-4 py-2 hover:bg-gray-100 dark:hover:bg-gray-800"
          >
            <FileText className="w-4 h-4 inline mr-2" />
            Parquet Viewer
          </button>
        </div>
        <div className="flex items-center gap-2 text-xs">
          <span className="text-muted-foreground hidden sm:inline">Role:</span>
          <div className="inline-flex rounded border bg-slate-100 dark:bg-slate-900">
            <button
              className={`px-2 py-1 rounded-l ${
                role === "Designer"
                  ? "bg-blue-600 text-white"
                  : "text-slate-700 dark:text-slate-200"
              }`}
              onClick={() => setRole("Designer")}
            >
              Designer
            </button>
            <button
              className={`px-2 py-1 rounded-r ${
                role === "PM"
                  ? "bg-amber-500 text-white"
                  : "text-slate-700 dark:text-slate-200"
              }`}
              onClick={() => setRole("PM")}
            >
              PM
            </button>
          </div>
        </div>
      </div>
      <div className="flex flex-col h-full p-4 gap-4 overflow-auto">
        <div className="flex-1">
          <div className="flex items-center gap-3 mb-4">
            <div className="w-8 h-8 bg-primary rounded flex items-center justify-center">
              <CircleDot className="text-primary-foreground" size={20} />
            </div>
            <div className="flex flex-col">
              <h1 className="text-2xl font-bold">PullRequest-For-Revit</h1>
              <span className="text-xs text-muted-foreground">
                Revit change visualization & sync guard
              </span>
            </div>
          </div>

        {/* Selection Display */}
        <div className="mb-4">
          <h2 className="text-lg font-semibold mb-2">
            Selected Elements ({selectedElements.length})
          </h2>
          {selectedElements.length === 0 ? (
            <p className="text-muted-foreground text-sm">
              No elements selected. Select elements in Revit to get started.
            </p>
          ) : (
            <div className="border rounded-md p-2 max-h-64 overflow-auto">
              <ul className="space-y-1">
                {selectedElements.map((element) => (
                  <li
                    key={element.id}
                    className="text-sm p-2 rounded hover:bg-accent"
                  >
                    <div className="font-medium">{element.name}</div>
                    <div className="text-xs text-muted-foreground">
                      {element.category} • {element.id.substring(0, 8)}...
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>

        {/* Status Messages */}
        {recordStatus && (
          <div className="mb-2 p-2 bg-blue-100 dark:bg-blue-900 rounded text-sm">
            {recordStatus}
          </div>
        )}
        {compareStatus && (
          <div className="mb-2 p-2 bg-green-100 dark:bg-green-900 rounded text-sm">
            {compareStatus}
          </div>
        )}
        {visualizationCreated === false && (
          <div className="mb-2 p-2 bg-yellow-100 dark:bg-yellow-900 rounded text-xs">
            Revit viewport visualization failed. Showing web-only visualization below.
          </div>
        )}
        {compareResults && compareResults.length > 0 && (
        <div
          className={
            !hasAnyCompareChanges
              ? "border-2 border-emerald-500 rounded-md p-2"
              : ""
          }
        >
            {/* Compare results filter */}
            <div className="sticky top-0 z-10 mb-2 flex items-center gap-2 bg-background/95 backdrop-blur border-b pb-2">
              <input
                type="text"
                value={compareFilter}
                onChange={(e) => setCompareFilter(e.target.value)}
                placeholder="Filter results by name, category, id, or change type..."
                className="flex-1 px-2 py-1 text-sm border rounded bg-background"
              />
              <span className="text-xs text-muted-foreground whitespace-nowrap">
                Showing{" "}
                {
                  compareResults.filter((r) =>
                    matchCompareFilter(r, compareFilter)
                  ).length
                }{" "}
                / {compareResults.length}
              </span>
              {role === "Designer" ? (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={!allChangedConfirmed(compareResults, confirmedIds)}
                  onClick={() => {
                    // Send to PM (stub on backend) and mark all changed items as submitted locally
                    sendMessage({ type: "compare:submitForReview" });
                    const changedIds = compareResults
                      .filter((r) => r.hasChanges)
                      .map((r) => r.uniqueId);
                    setSubmittedIds(changedIds);
                    setApprovedIds([]);
                    setRejectedIds([]);
                    setCompareStatus("Review request has been submitted to PM.");
                    setTimeout(() => setCompareStatus(null), 3000);
                  }}
                >
                  Submit for Review
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={!allChangedReviewed(compareResults, approvedIds, rejectedIds)}
                  onClick={() => {
                    // PM has resolved all items (approved or rejected) and allows sync
                    sendMessage({ type: "compare:confirmAll" });
                    setCompareStatus("PM review complete. Sync is now allowed.");
                    setTimeout(() => setCompareStatus(null), 3000);
                  }}
                >
                  Allow Sync
                </Button>
              )}
            </div>

            <WebComparisonViewer
              role={role}
              results={compareResults.filter((r) =>
                matchCompareFilter(r, compareFilter)
              )}
              confirmedIds={confirmedIds}
              submittedIds={submittedIds}
              approvedIds={approvedIds}
              rejectedIds={rejectedIds}
              onToggleConfirm={(id, confirmed) => {
                setConfirmedIds((prev) =>
                  confirmed ? [...prev, id] : prev.filter((x) => x !== id)
                );
              }}
              onSetDecision={(id, decision) => {
                if (decision === "approved") {
                  setApprovedIds((prev) => (prev.includes(id) ? prev : [...prev, id]));
                  setRejectedIds((prev) => prev.filter((x) => x !== id));
                } else if (decision === "rejected") {
                  setRejectedIds((prev) => (prev.includes(id) ? prev : [...prev, id]));
                  setApprovedIds((prev) => prev.filter((x) => x !== id));
                } else {
                  setApprovedIds((prev) => prev.filter((x) => x !== id));
                  setRejectedIds((prev) => prev.filter((x) => x !== id));
                }
              }}
            />
          </div>
        )}
        </div>

        {/* Action Buttons */}
        <div className="flex flex-col gap-2 border-t pt-4">
          <Button
            onClick={handleRecord}
            disabled={isRecording || selectedElements.length === 0}
            className="w-full"
          >
            {isRecording ? (
              <>
                <Circle className="mr-2 animate-pulse fill-current" size={16} />
                Recording...
              </>
            ) : (
              <>
              <CircleDot className="mr-2" size={16} />
              Record Elements
              </>
            )}
          </Button>

          <Button
            onClick={handleCompare}
            disabled={isComparing}
            variant="secondary"
            className="w-full"
          >
            {isComparing ? (
              <>
                <Circle className="mr-2 animate-pulse fill-current" size={16} />
                Comparing...
              </>
            ) : (
              <>
                <GitCompare className="mr-2" size={16} />
                Compare Elements
              </>
            )}
          </Button>

          <Button
            onClick={handleClearVisualization}
            variant="outline"
            className="w-full"
          >
            <Trash2 className="mr-2" size={16} />
            Clear Visualization
          </Button>
        </div>
      </div>

      {/* Global notifications (top-right) */}
      {notifications.length > 0 && (
        <div className="fixed top-4 right-4 z-50 space-y-2">
          {notifications.map((n) => (
            <div
              key={n.id}
              className={`px-3 py-2 rounded shadow text-xs text-white ${
                n.level === "success"
                  ? "bg-emerald-600"
                  : n.level === "warning"
                  ? "bg-amber-500"
                  : n.level === "error"
                  ? "bg-red-600"
                  : "bg-slate-700"
              }`}
            >
              {n.message}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function matchCompareFilter(result: CompareResult, filter: string): boolean {
  if (!filter.trim()) return true;
  const q = filter.toLowerCase();
  const parts = [
    result.uniqueId,
    result.name,
    result.category,
    result.changeType,
    ...(result.parameterChanges?.map((p) => p.name) ?? []),
  ];
  return parts.some((p) => p && p.toLowerCase().includes(q));
}

function allChangedConfirmed(results: CompareResult[], confirmedIds: string[]): boolean {
  const changed = results.filter((r) => r.hasChanges);
  if (changed.length === 0) return false;
  return changed.every((r) => confirmedIds.includes(r.uniqueId));
}

function allChangedReviewed(
  results: CompareResult[],
  approvedIds: string[],
  rejectedIds: string[]
): boolean {
  const changed = results.filter((r) => r.hasChanges);
  if (changed.length === 0) return false;
  return changed.every(
    (r) => approvedIds.includes(r.uniqueId) || rejectedIds.includes(r.uniqueId)
  );
}

