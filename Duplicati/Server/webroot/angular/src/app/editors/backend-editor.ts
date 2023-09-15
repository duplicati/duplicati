import { EventEmitter, InjectionToken, Type } from "@angular/core";

export interface CommonBackendData {
  username?: string;
  password?: string;
  useSSL?: boolean;
  port?: string;
  server?: string;
  path?: string;
}

export const BACKEND_EDITORS = new InjectionToken<({ key: string, type: Type<BackendEditorComponent> })[]>('backend editors', {
  providedIn: 'root',
  factory: () => []
});

export interface BackendEditorComponent {
  commonData: CommonBackendData;
  commonDataChange: EventEmitter<CommonBackendData>;

  // Parse URI before extracting advanced options. Remove used parts from the map and may update data
  parseUriParts(data: CommonBackendData, parts: Map<string, string>): void;
  buildUri(advancedOptions: string[]): string | undefined;
  extraConnectionTests(): boolean;
}
