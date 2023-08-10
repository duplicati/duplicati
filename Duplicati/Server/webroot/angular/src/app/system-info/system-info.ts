export interface CommandLineArgument {
  Aliases: string[] | null;
  LongDescription: string;
  Name: string;
  ShortDescription: string;
  Type: string;
  ValidValues: string[] | null;
  DefaultValue: string | null;
  Typename: string;
  Deprecated: boolean;
  DeprecationMessage: string;

  Category?: string;
}

export interface ModuleDescription {
  Key: string,
  Description: string,
  DisplayName: string,
  Options: CommandLineArgument[] | null
}

export interface BrowserLocale {
  Code: string,
  EnglishName: string,
  DisplayName: string
}

export interface SystemInfo {
  // TODO: Fix any
  APIVersion: number,
  BackendModules: ModuleDescription[],
  BaseVersionName: string,
  BrowserLocale: BrowserLocale,
  BrowserLocaleSupported: boolean,
  CLROSInfo: any,
  CLRVersion: string,
  CaseSensitiveFilesystem: boolean,
  CompressionModules: ModuleDescription[],
  ConnectionModules: any,
  DefaultUpdateChannel: string,
  DefaultUsageReportLevel: string,
  DirectorySeparator: string,
  EncryptionModules: ModuleDescription[],
  GenericModules: ModuleDescription[],
  GroupTypes: string[],
  GroupedBackendModules: any[],
  LogLevels: string[],
  MachineName: string,
  MonoVersion: string | null,
  NewLine: string,
  OSType: string,
  Options: CommandLineArgument[],
  PasswordPlaceholder: string,
  PathSeparator: string,
  ServerModules: any[],
  ServerTime: string,
  ServerVersion: string,
  ServerVersionName: string,
  ServerVersionType: string,
  SpecialFolders: any[],
  StartedBy: string,
  SupportedLocales: any[],
  SuppressDonationMessages: boolean,
  UserName: string,
  UsingAlternateUpdateURLs: boolean,
  WebModules: any[],
  backendgroups: any,

}
