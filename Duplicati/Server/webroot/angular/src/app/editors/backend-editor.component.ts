import { EventEmitter, InjectionToken, Type } from "@angular/core";

export const BACKEND_EDITORS = new InjectionToken<({ key: string, type: Type<BackendEditorComponent> })[]>('backend editors', {
  providedIn: 'root',
  factory: () => []
});

export interface BackendEditorComponent {
  path: string;
  pathChange: EventEmitter<string>;
  username: string;
  usernameChange: EventEmitter<string>;
  password: string;
  passwordChange: EventEmitter<string>;
}
