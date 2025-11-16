// Message types matching the C# backend models

export interface ElementInfo {
  id: string;
  name: string;
  category: string;
}

export interface SelectionChangedMessage {
  type: "selection:changed";
  elements: ElementInfo[];
}

export interface RecordCompleteMessage {
  type: "record:complete";
  success: boolean;
  results: RecordResult[];
  error?: string;
}

export interface RecordResult {
  uniqueId: string;
  success: boolean;
  error?: string;
}

export interface CompareCompleteMessage {
  type: "compare:complete";
  success: boolean;
  results: CompareResult[];
  visualizationCreated?: boolean;
  error?: string;
}

export interface XYZData {
  x: number;
  y: number;
  z: number;
}

export interface BoundingBoxData {
  min: XYZData;
  max: XYZData;
}

export interface ParameterChange {
  name: string;
  oldValue: any;
  newValue: any;
}

export interface CompareResult {
  uniqueId: string;
  name: string;
  category: string;
  hasChanges: boolean;
  changeType: string;
  translation?: XYZData;
  parameterChanges: ParameterChange[];
  isDeleted: boolean;
  recordedBoundingBox?: BoundingBoxData | null;
  currentBoundingBox?: BoundingBoxData | null;
  error?: string;
}

export interface NotificationMessage {
  type: "notification";
  level: "info" | "success" | "warning" | "error";
  message: string;
}

export type IncomingMessage =
  | SelectionChangedMessage
  | RecordCompleteMessage
  | CompareCompleteMessage
  | ParquetFilesResponse
  | ParquetFileInfoResponse
  | ParquetDataResponse
  | NotificationMessage;

export interface RecordMessage {
  type: "record:elements";
  elementIds?: string[];
}

export interface CompareMessage {
  type: "compare:elements";
  elementIds?: string[];
}

export interface CompareConfirmAllMessage {
  type: "compare:confirmAll";
}

export interface CompareSubmitForReviewMessage {
  type: "compare:submitForReview";
}

export interface CompareRejectChangesMessage {
  type: "compare:rejectChanges";
}

export interface ClearVisualizationMessage {
  type: "clear:visualization";
}

export interface GetSelectionMessage {
  type: "get:selection";
}

export interface GetParquetFilesMessage {
  type: "parquet:get:files";
}

export interface GetParquetFileInfoMessage {
  type: "parquet:get:info";
  fileName: string;
}

export interface GetParquetDataMessage {
  type: "parquet:get:data";
  fileName: string;
  offset?: number;
  limit?: number;
  sortColumn?: string;
  ascending?: boolean;
}

export interface ParquetColumnInfo {
  name: string;
  dataType: string;
  isNullable: boolean;
}

export interface ParquetFileInfo {
  fileName: string;
  rowCount: number;
  columnCount: number;
  columns: ParquetColumnInfo[];
  fileSize: number;
  rowGroupCount: number;
}

export interface ParquetFilesResponse {
  type: "parquet:files:response";
  files: string[];
  error?: string;
}

export interface ParquetFileInfoResponse {
  type: "parquet:info:response";
  info?: ParquetFileInfo;
  error?: string;
}

export interface ParquetDataResponse {
  type: "parquet:data:response";
  rows: Record<string, any>[];
  columns: string[];
  totalRows: number;
  offset: number;
  limit: number;
  error?: string;
}

export type OutgoingMessage =
  | RecordMessage
  | CompareMessage
  | CompareConfirmAllMessage
  | CompareSubmitForReviewMessage
  | CompareRejectChangesMessage
  | ClearVisualizationMessage
  | GetSelectionMessage
  | GetParquetFilesMessage
  | GetParquetFileInfoMessage
  | GetParquetDataMessage;

