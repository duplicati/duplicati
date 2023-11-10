export interface ServerLogEntry {
  BackupID?: number;
  Message: string;
  Exception?: string;
  Timestamp: number;
}

export interface BackupLogEntry {
  ID: number;
  OperationID: number;
  Timestamp: number;
  Type: string;
  Message: string;
  Exception?: string;

  // Parsed by client:
  Result?: any;
  Formatted?: string;
}

export interface RemoteLogEntry {
  ID: number;
  OperationID: number;
  Timestamp: number;
  Operation: string;
  Path: string;
  Data?: string;
}

export interface LiveLogEntry {
  ID: number;
  When: string;
  Message: string;
  Tag: string;
  MessageID: string;
  ExceptionID: string;
  Type: string;
  Exception: any;
  BackupID?: string;
  TaskID?: string;
}
