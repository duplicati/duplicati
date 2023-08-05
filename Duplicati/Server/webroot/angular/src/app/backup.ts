export interface Schedule {
  ID: number,
  Tags: string[],
  Time: string,
  Repeat: string,
  LastRun: string,
  Rule: string,
  AllowedDays: string[]
}

export interface Backup {
  ID: string,
  Name: string,
  Description: string,
  Tags: string[],
  TargetURL: string,
  DBPath: string,
  Sources: string[],
  Settings: any[],
  Filters: any[],
  Metadata: Map<string, string>,
  IsTemporary: boolean
}

export interface AddOrUpdateBackupData {
  IsUnencryptedOrPassphraseStored: boolean,
  Schedule: Schedule | null,
  Backup: Backup
}
