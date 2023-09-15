import { inject, Inject, Injectable, Type } from '@angular/core';
import { BackendEditorComponent, BACKEND_EDITORS, CommonBackendData } from '../editors/backend-editor';
import { GroupedModuleDescription } from '../system-info/system-info';
import { ParserService } from './parser.service';

@Injectable({
  providedIn: 'root'
})
export class EditUriService {
  private editors = new Map<string, Type<BackendEditorComponent>>();

  constructor(@Inject(BACKEND_EDITORS) editorTypes: ({ key: string, type: Type<BackendEditorComponent> })[],
    private parser: ParserService) {
    for (let e of editorTypes) {
      this.editors.set(e.key, e.type);
    }
  }

  get defaultbackend(): string {
    return 'file';
  }

  getEditorType(key: string): Type<BackendEditorComponent> | undefined {
    return this.editors.get(key);
  }

  parseUri(uri: string, backends?: GroupedModuleDescription[], parser?: (data: CommonBackendData, parts: Map<string, string>) => void): { backend: GroupedModuleDescription | undefined, data: CommonBackendData, advanced: string[] } {
    let parts = this.parser.decodeBackendUri(uri);

    let result: { backend: GroupedModuleDescription | undefined, data: CommonBackendData, advanced: string[] } = {
      backend: undefined,
      data: {},
      advanced: []
    };

    const backendType = parts.get('backend-type');
    if (backendType != null && backends != null) {
      for (let m of backends) {
        if (m.Key === backendType) {
          result.backend = m;
          break;
        }

        if (m.Key + 's' === backendType) {
          // Try to enable SSL
          if (m.Options?.find(o => o.Name === 'use-ssl') != null) {
            result.backend = m;
            parts.set('--use-ssl', 'true');
          }
        }
      }
    }

    result.data = {
      username: parts.get('--auth-username'),
      password: parts.get('--auth-password'),
      port: parts.get('server-port'),
      server: parts.get('server-name'),
      path: parts.get('server-path')
    };

    if (parts.has('--use-ssl')) {
      result.data.useSSL = this.parser.parseBoolString(parts.get('--use-ssl'));
    }

    if (parser != null) {
      parser(result.data, parts);
    }

    parts.delete('--auth-username');
    parts.delete('--auth-password');
    parts.delete('--use-ssl');
    result.advanced = this.parser.serializeAdvancedOptionsToArray(parts);
    return result;
  }

  mergeAdvancedOptions(commonData: CommonBackendData, advanced: string[], dict: Record<string, string>): boolean {
    if (commonData.username != null && commonData.username != '') {
      dict['auth-username'] = commonData.username;
    }
    if (commonData.password != null && commonData.password != '') {
      dict['auth-password'] = commonData.password;
    }
    if (!this.parser.parseExtraOptions(advanced, dict)) {
      return false;
    }

    for (let k in dict) {
      if (k.startsWith('--')) {
        dict[k.substr(2)] = dict[k];
        delete dict[k];
      }
    }
    return true;
  }
}
