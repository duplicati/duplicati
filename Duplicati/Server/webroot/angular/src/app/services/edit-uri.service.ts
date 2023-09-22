import { Optional } from '@angular/core';
import { Inject, Injectable, Injector, Type } from '@angular/core';
import { BackendEditorComponent, BACKEND_EDITORS, BACKEND_KEY, BACKEND_SUPPORTS_SSL, CommonBackendData, DEFAULT_BACKEND_EDITOR } from '../backend-editor';
import { GroupedModuleDescription, ModuleDescription } from '../system-info/system-info';
import { DialogService } from './dialog.service';
import { ParserService } from './parser.service';

@Injectable({
  providedIn: 'root'
})
export class EditUriService {
  private editors = new Map<string, Type<BackendEditorComponent>>();

  constructor(@Inject(BACKEND_EDITORS) editorTypes: ({ key: string, type: Type<BackendEditorComponent> })[],
    @Inject(DEFAULT_BACKEND_EDITOR) @Optional() private defaultEditor: Type<BackendEditorComponent> | null,
    private dialog: DialogService,
    private parser: ParserService) {
    for (let e of editorTypes) {
      this.editors.set(e.key, e.type);
    }
  }

  get defaultbackend(): string {
    return 'file';
  }

  getEditorType(key: string): Type<BackendEditorComponent> | null {
    return this.editors.get(key) ?? this.defaultEditor;
  }

  createEditorInjector(backend: ModuleDescription, parent: Injector): Injector {
    return Injector.create({
      providers: [
        { provide: BACKEND_KEY, useValue: backend.Key || '' },
        { provide: BACKEND_SUPPORTS_SSL, useValue: this.isSslSupported(backend) }
      ],
      parent: parent,
      name: 'Backend editor injector'
    });
  }

  getBackend(uri: string, backends: GroupedModuleDescription[]): GroupedModuleDescription | null {
    const backendType = this.parser.decodeBackendUri(uri).get('backend-type') ?? null;
    if (backendType != null) {
      for (let m of backends) {
        if (m.Key === backendType) {
          return m;
        }
      }
    }
    return null;
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

  mergeServerAndPath(commonData: CommonBackendData) {
    if ((commonData.server || '') != '') {
      let p = commonData.path;
      commonData.path = commonData.server;
      if ((p || '') != '') {
        commonData.path += '/' + p;
      }
      commonData.server = undefined;
    }
  }

  requireField<Data>(data: Data, field: keyof (Data) & string, label?: string): boolean {
    if (((data[field] || '') as string).trim().length == 0) {
      this.dialog.dialog($localize`Error`, $localize`You must fill in ${label || field}`);
      return false;
    }
    return true;
  }

  invalidCharacterError(fieldName: string, character: string, charCode: number, index: number) {
    this.dialog.dialog($localize`Error`, $localize`The '${fieldName}' field contains an invalid character: ${character} (value: ${charCode}, index: ${index})`);
  }

  recommendField<Data>(data: Data, field: keyof (Data) & string, warning: string): boolean {
    if (((data[field] || '') as string).trim().length == 0) {
      this.dialog.dialog($localize`Confirmation required`, warning, [$localize`No`, $localize`Yes`],
        (ix) => {
          if (ix == 1) {
            //continuation()
          }
        });
      return false;
    }
    return true;
  }

  isSslSupported(backend: ModuleDescription | undefined): boolean {
    return backend?.Options?.find(opt => opt.Name == 'use-ssl') != null || false;
  }
}
