export interface SmartRetention {
  type: 'smart';
}
export interface CustomRetention {
  type: 'custom';
  policy: string;
}
export interface VersionsRetention {
  type: 'versions';
  keepVersions: number;
}
export interface TimeRetention {
  type: 'time';
  keepTime: string;
}

export class BackupOptions {
  passphrase: string = '';
  repeatPassphrase: string = '';
  encryptionModule: string = '';
  extendedOptions: string[] = [];
  hasGeneratedPassphrase: boolean = false;
  dblockSize: string = '50MB';
  retention: SmartRetention | CustomRetention | VersionsRetention | TimeRetention | null = null;
  serverModuleSettings: Map<string, string> | null = null;
  compressionModule: string = 'zip';
  excludeFileSize: number | null = null;
  excludeFileAttributes: string[] = [];
  isNew: boolean = true;


}
