import React, { useState, useRef } from "react";
import type { CompareResult } from "@/types/messages";
import ThreeBoxViewer from "./ThreeBoxViewer";

interface WebComparisonViewerProps {
  role: "Designer" | "PM";
  results: CompareResult[];
  confirmedIds: string[];
  submittedIds: string[];
  approvedIds: string[];
  rejectedIds: string[];
  onToggleConfirm: (id: string, confirmed: boolean) => void;
  onSetDecision: (id: string, decision: "approved" | "rejected" | "none") => void;
}

// Renders per-element cards with a simple "3D-style" before/after bounding box
// visualization and a textual summary of what changed. This is purely web-based
// and does not depend on the Revit graphics API.
export default function WebComparisonViewer({
  role,
  results,
  confirmedIds,
  submittedIds,
  approvedIds,
  rejectedIds,
  onToggleConfirm,
  onSetDecision,
}: WebComparisonViewerProps) {
  const changed = results.filter((r) => r.hasChanges);

  if (changed.length === 0) {
    return null;
  }

  return (
    <div className="mt-3 space-y-3">
      {changed.map((r) => (
        <ComparisonCard
          key={r.uniqueId}
          role={role}
          result={r}
          confirmed={confirmedIds.includes(r.uniqueId)}
          submitted={submittedIds.includes(r.uniqueId)}
          approved={approvedIds.includes(r.uniqueId)}
          rejected={rejectedIds.includes(r.uniqueId)}
          onToggleConfirm={onToggleConfirm}
          onSetDecision={onSetDecision}
        />
      ))}
    </div>
  );
}

interface ComparisonCardProps {
  role: "Designer" | "PM";
  result: CompareResult;
  confirmed: boolean;
  submitted: boolean;
  approved: boolean;
  rejected: boolean;
  onToggleConfirm: (id: string, confirmed: boolean) => void;
  onSetDecision: (id: string, decision: "approved" | "rejected" | "none") => void;
}

function ComparisonCard({
  role,
  result,
  confirmed,
  submitted,
  approved,
  rejected,
  onToggleConfirm,
  onSetDecision,
}: ComparisonCardProps) {
  const [viewMode, setViewMode] = useState<"2d" | "3d">("2d");

  return (
    <div className="border rounded-md p-3 bg-slate-50 dark:bg-slate-900">
      <div className="flex justify-between items-center mb-2">
        <div>
          <div className="text-sm font-semibold">
            {result.name || "Unnamed element"}
          </div>
          <div className="text-xs text-muted-foreground">
            {result.category} • {result.uniqueId.substring(0, 8)}...
          </div>
        </div>
        <div className="flex flex-col items-end gap-1">
          <div className="flex gap-1">
            <div className="text-xs px-2 py-1 rounded bg-blue-100 dark:bg-blue-900 text-blue-800 dark:text-blue-100">
              {result.changeType.toUpperCase()}
            </div>
            {submitted && !approved && !rejected && (
              <div className="text-xs px-2 py-1 rounded bg-amber-100 dark:bg-amber-900 text-amber-800 dark:text-amber-100">
                SUBMITTED
              </div>
            )}
            {approved && (
              <div className="text-xs px-2 py-1 rounded bg-emerald-100 dark:bg-emerald-900 text-emerald-800 dark:text-emerald-100">
                APPROVED
              </div>
            )}
            {rejected && (
              <div className="text-xs px-2 py-1 rounded bg-red-100 dark:bg-red-900 text-red-800 dark:text-red-100">
                REJECTED
              </div>
            )}
          </div>
          <div className="inline-flex rounded border bg-slate-100 dark:bg-slate-900 text-[10px]">
            <button
              className={`px-2 py-0.5 rounded-l ${
                viewMode === "2d"
                  ? "bg-blue-600 text-white"
                  : "text-slate-700 dark:text-slate-200"
              }`}
              onClick={() => setViewMode("2d")}
            >
              2D
            </button>
            <button
              className={`px-2 py-0.5 rounded-r ${
                viewMode === "3d"
                  ? "bg-blue-600 text-white"
                  : "text-slate-700 dark:text-slate-200"
              }`}
              onClick={() => setViewMode("3d")}
            >
              3D
            </button>
          </div>
        </div>
      </div>

      {/* Overlapped before/after visualization in a single workspace */}
      <OverlapBoxView
        recorded={result.recordedBoundingBox || undefined}
        current={result.currentBoundingBox || undefined}
        viewMode={viewMode}
      />

      {/* Summary of changes */}
      <div className="text-xs text-muted-foreground space-y-1">
        {result.translation && (() => {
          const tx = result.translation.x;
          const ty = result.translation.y;
          const tz = result.translation.z;
          const hasAllNumbers =
            typeof tx === "number" && Number.isFinite(tx) &&
            typeof ty === "number" && Number.isFinite(ty) &&
            typeof tz === "number" && Number.isFinite(tz);

          if (!hasAllNumbers) {
            // If translation exists but isn't clean numeric data, hide the position line
            // instead of showing a confusing fallback.
            return null;
          }

          return (
            <div>
              <span className="font-semibold">Position:</span>{" "}
              moved by Δ(
              {tx.toFixed(3)},{" "}
              {ty.toFixed(3)},{" "}
              {tz.toFixed(3)})
            </div>
          );
        })()}

        {result.parameterChanges && result.parameterChanges.length > 0 && (
          <div>
            <span className="font-semibold">
              Parameters ({result.parameterChanges.length}):
            </span>
            <ul className="mt-1 list-disc list-inside space-y-0.5">
              {result.parameterChanges.slice(0, 3).map((p) => (
                <li key={p.name}>
                  <span className="font-semibold">{p.name}:</span>{" "}
                  <span className="line-through opacity-70">
                    {formatParamValue(p.oldValue)}
                  </span>{" "}
                  <span className="mx-1">→</span>
                  <span>{formatParamValue(p.newValue)}</span>
                </li>
              ))}
              {result.parameterChanges.length > 3 && (
                <li className="text-[10px] text-muted-foreground">
                  … and {result.parameterChanges.length - 3} more parameter change(s)
                </li>
              )}
            </ul>
          </div>
        )}

        {result.isDeleted && (
          <div className="text-red-600 dark:text-red-400 font-semibold">
            Element was deleted in current document.
          </div>
        )}

        {/* Per-element confirmation checkbox (Designer role only) */}
        {role === "Designer" && (
          <label className="mt-2 inline-flex items-center gap-2 text-[11px] cursor-pointer">
            <input
              type="checkbox"
              checked={confirmed}
              onChange={(e) => onToggleConfirm(result.uniqueId, e.target.checked)}
            />
            <span>Confirm change for this element</span>
          </label>
        )}

        {/* PM per-element decision controls */}
        {role === "PM" && (
          <div className="mt-2 flex items-center gap-2 text-[11px]">
            <span className="text-muted-foreground">PM decision:</span>
            <button
              className={`px-2 py-0.5 rounded border text-[11px] ${
                approved ? "bg-emerald-600 text-white border-emerald-700" : "border-slate-400"
              }`}
              onClick={() =>
                onSetDecision(
                  result.uniqueId,
                  approved ? "none" : "approved"
                )
              }
            >
              Approve
            </button>
            <button
              className={`px-2 py-0.5 rounded border text-[11px] ${
                rejected ? "bg-red-600 text-white border-red-700" : "border-slate-400"
              }`}
              onClick={() =>
                onSetDecision(
                  result.uniqueId,
                  rejected ? "none" : "rejected"
                )
              }
            >
              Reject
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

interface BoxViewProps {
  recorded?: CompareResult["recordedBoundingBox"];
  current?: CompareResult["currentBoundingBox"];
  viewMode: "2d" | "3d";
}

function OverlapBoxView({ recorded, current, viewMode }: BoxViewProps) {
  if (!recorded && !current) {
    return (
      <div className="flex flex-col items-center justify-center text-xs text-muted-foreground border rounded-md py-6 mb-2">
        <div className="mb-1 font-semibold">Geometry</div>
        <div>No geometry for this element</div>
      </div>
    );
  }

  // 3D mode: use Three.js-based viewer for better interaction
  if (viewMode === "3d") {
    return (
      <div className="flex flex-col text-xs mb-2 gap-1">
        <div className="mb-1 font-semibold">Before / After (3D viewer)</div>
        <ThreeBoxViewer recorded={recorded} current={current} />
        <div className="text-[10px] text-muted-foreground">
          Blue = original, red = current. Drag to orbit, scroll to zoom, Shift+drag to pan.
        </div>
      </div>
    );
  }

  // 2D mode: three orthographic overlapping projections
  return (
    <div className="flex flex-col text-xs mb-2 gap-2">
      <div className="mb-1 font-semibold">Before / After (overlapped views)</div>
      <div className="grid grid-cols-3 gap-2">
        <ProjectionView
          title="Top (XY)"
          recorded={recorded}
          current={current}
          axes={{ x: "x", y: "y" }}
        />
        <ProjectionView
          title="Front (XZ)"
          recorded={recorded}
          current={current}
          axes={{ x: "x", y: "z" }}
        />
        <ProjectionView
          title="Side (YZ)"
          recorded={recorded}
          current={current}
          axes={{ x: "y", y: "z" }}
        />
      </div>
      <div className="text-[10px] text-muted-foreground mt-1">
        Blue dashed = original, red filled = current. Views are projections of the 3D bounding box.
      </div>
    </div>
  );
}

interface ProjectionViewProps {
  title: string;
  recorded?: CompareResult["recordedBoundingBox"];
  current?: CompareResult["currentBoundingBox"];
  axes: { x: "x" | "y" | "z"; y: "x" | "y" | "z" };
}

function ProjectionView({ title, recorded, current, axes }: ProjectionViewProps) {
  const isFiniteNumber = (v: any): v is number =>
    typeof v === "number" && Number.isFinite(v);

  const isValidBox = (b: CompareResult["recordedBoundingBox"] | undefined | null): b is NonNullable<CompareResult["recordedBoundingBox"]> => {
    if (!b || !b.min || !b.max) return false;
    const coords = [
      b.min.x,
      b.min.y,
      b.min.z,
      b.max.x,
      b.max.y,
      b.max.z,
    ];
    return coords.every(isFiniteNumber);
  };

  const boxes = [recorded, current].filter(isValidBox);

  if (boxes.length === 0) {
    return (
      <div className="border rounded bg-slate-100 dark:bg-slate-950 flex items-center justify-center text-[10px] text-muted-foreground h-24">
        {title}
      </div>
    );
  }

  const getCoord = (b: NonNullable<CompareResult["recordedBoundingBox"]>, key: "min" | "max") => {
    const xyz = b[key];
    return axes.x === "x"
      ? xyz.x
      : axes.x === "y"
      ? xyz.y
      : xyz.z;
  };

  const getCoordY = (b: NonNullable<CompareResult["recordedBoundingBox"]>, key: "min" | "max") => {
    const xyz = b[key];
    return axes.y === "x"
      ? xyz.x
      : axes.y === "y"
      ? xyz.y
      : xyz.z;
  };

  const minX = Math.min(...boxes.map((b) => getCoord(b, "min")));
  const maxX = Math.max(...boxes.map((b) => getCoord(b, "max")));
  const minY = Math.min(...boxes.map((b) => getCoordY(b, "min")));
  const maxY = Math.max(...boxes.map((b) => getCoordY(b, "max")));

  const dxRaw = maxX - minX;
  const dyRaw = maxY - minY;

  if (!Number.isFinite(dxRaw) || !Number.isFinite(dyRaw)) {
    return (
      <div className="border rounded bg-slate-100 dark:bg-slate-950 flex items-center justify-center text-[10px] text-muted-foreground h-24">
        {title}
      </div>
    );
  }

  const dx = dxRaw || 1;
  const dy = dyRaw || 1;
  const pad = 0.1 * Math.max(Math.abs(dx), Math.abs(dy));

  const viewMinX = minX - pad;
  const viewMinY = minY - pad;
  const viewWidth = dx + 2 * pad;
  const viewHeight = dy + 2 * pad;

  const projectRect = (b: NonNullable<CompareResult["recordedBoundingBox"]>) => ({
    x: getCoord(b, "min"),
    y: viewMinY + viewHeight - getCoordY(b, "max") + viewMinY, // flip Y for display
    width: getCoord(b, "max") - getCoord(b, "min"),
    height: getCoordY(b, "max") - getCoordY(b, "min"),
  });

  const strokeWidth = viewWidth * 0.003;

  return (
    <div className="flex flex-col gap-1">
      <div className="text-[10px] text-muted-foreground">{title}</div>
      <svg
        className="w-full h-24 border rounded bg-slate-100 dark:bg-slate-950"
        viewBox={`${viewMinX} ${viewMinY} ${viewWidth} ${viewHeight}`}
        preserveAspectRatio="xMidYMid meet"
      >
        {recorded && (() => {
          const r = projectRect(recorded);
          return (
            <rect
              x={r.x}
              y={r.y}
              width={r.width}
              height={r.height}
              fill="none"
              stroke="#1d4ed8"
              strokeWidth={strokeWidth}
              strokeDasharray="4 2"
            />
          );
        })()}
        {current && (() => {
          const c = projectRect(current);
          return (
            <rect
              x={c.x}
              y={c.y}
              width={c.width}
              height={c.height}
              fill="rgba(239,68,68,0.35)"
              stroke="#ef4444"
              strokeWidth={strokeWidth}
            />
          );
        })()}
      </svg>
    </div>
  );
}

function formatParamValue(value: any): string {
  if (value === null || value === undefined || value === "") return "[Empty]";
  if (typeof value === "number") return value.toString();
  if (typeof value === "string") return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}


