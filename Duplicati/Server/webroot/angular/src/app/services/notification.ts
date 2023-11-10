export interface Notification {
  ID: number;
  Type: 'Information' | 'Warning' | 'Error';
  Title: string;
  Message: string;
  Exception: string;
  BackupID: string;
  Action: string;
  Timestamp: string;
  LogEntryID: string;
  MessageID: string;
  MessageLogTag: string;
  // Used to download bug reports, not sent by server
  DownloadLink?: string;
}
