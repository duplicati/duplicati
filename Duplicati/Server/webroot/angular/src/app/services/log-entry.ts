export interface ServerLogEntry {
  BackupID?: number;
  Message: string;
  Exception?: string;
  Timestamp: number;
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
