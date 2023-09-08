import { CommandLineArgument } from "./system-info/system-info";

export interface Schedule {
  ID?: number,
  Tags: string[],
  Time?: string,
  Repeat: string,
  LastRun?: string,
  Rule?: string,
  AllowedDays: string[]
}

export interface BackupSetting {
  Filter: string,
  Name: string,
  Value: string,
  Argument: CommandLineArgument | null
}

export interface BackupFilter {
  Order: number,
  Include: boolean,
  Expression: string
}

export interface Backup {
  ID: string,
  Name: string,
  Description: string,
  Tags: string[],
  TargetURL: string,
  DBPath: string,
  Sources: string[],
  Settings: BackupSetting[],
  Filters: BackupFilter[],
  Metadata: Record<string, string>,
  IsTemporary: boolean
}

export interface AddOrUpdateBackupData {
  IsUnencryptedOrPassphraseStored?: boolean,
  Schedule: Schedule | null,
  Backup: Backup
}
