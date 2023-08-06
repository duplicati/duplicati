export interface OptionDescription {
  Name: string,
  Aliases: string | null,
  Type: string,
  Typename: string,
  ValidValues: string[] | null,
  ShortDescription: string,
  LongDescription: string,
  Deprecated: boolean,
  DeprecationMessage: string,
  DefaultValue: string
}

export interface BackendModule {
  Key: string,
  Description: string,
  DisplayName: string,
  Options: OptionDescription[]
}

export interface BrowserLocale {
  Code: string,
  EnglishName: string,
  DisplayName: string
}

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
  DeprectaionMessage: string;

  Category?: string;
}

export interface SystemInfo {
  // TODO: Fix any
  APIVersion: number,
  BackendModules: BackendModule[],
  BaseVersionName: string,
  BrowserLocale: BrowserLocale,
  BrowserLocaleSupported: boolean,
  CLROSInfo: any,
  CLRVersion: string,
  CaseSensitiveFilesystem: boolean,
  CompressionModules: any,
  ConnectionModules: any,
  DefaultUpdateChannel: string,
  DefaultUsageReportLevel: string,
  DirectorySeparator: string,
  EncryptionModules: any[],
  GenericModules: any[],
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
