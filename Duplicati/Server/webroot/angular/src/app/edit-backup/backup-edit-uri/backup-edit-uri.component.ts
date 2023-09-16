import { ComponentRef, Injector, SimpleChanges } from '@angular/core';
import { Component, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { DynamicHostDirective } from '../../directives/dynamic-host.directive';
import { BackendEditorComponent, CommonBackendData } from '../../backend-editor';
import { ConnectionTester } from '../../services/connection-tester.service';
import { ConvertService } from '../../services/convert.service';
import { EditUriService } from '../../services/edit-uri.service';
import { GroupedOptions, GroupedOptionService } from '../../services/grouped-option.service';
import { ParserService } from '../../services/parser.service';
import { CommandLineArgument, GroupedModuleDescription, ModuleDescription, SystemInfo } from '../../system-info/system-info';
import { SystemInfoService } from '../../system-info/system-info.service';
import { map, Observable } from 'rxjs';
import { EMPTY } from 'rxjs';


@Component({
  selector: 'app-backup-edit-uri',
  templateUrl: './backup-edit-uri.component.html',
  styleUrls: ['./backup-edit-uri.component.less']
})
export class BackupEditUriComponent {
  @Input({ alias: 'uri' }) set uriInput(v: string) {
    this.setUri(v);
  }
  uri?: string;
  backend?: ModuleDescription;
  defaultBackend?: ModuleDescription;
  editorComponent?: ComponentRef<BackendEditorComponent>;
  groupedBackendModules: GroupedOptions<ModuleDescription> = [];
  testing: boolean = false;
  showAdvancedTextArea: boolean = false;
  advancedOptions: string[] = [];
  advancedOptionList: CommandLineArgument[] = [];

  uriParts?: Map<string, string>;
  commonData?: CommonBackendData;

  private backendSupportsSsl: boolean = false;

  private systemInfo?: SystemInfo;

  @ViewChild(DynamicHostDirective, { static: true }) editorHost!: DynamicHostDirective;

  constructor(public parser: ParserService,
    public convert: ConvertService,
    private injector: Injector,
    private systemInfoService: SystemInfoService,
    private editUriService: EditUriService,
    private groupService: GroupedOptionService,
    private connectionTester: ConnectionTester) {
  }

  ngOnInit() {
    this.systemInfoService.getState().subscribe(s => {
      this.defaultBackend = undefined;
      this.systemInfo = s;
      if (s.GroupedBackendModules) {
        for (let m of s.GroupedBackendModules) {
          if (m.Key === this.editUriService.defaultbackend) {
            this.defaultBackend = m;
          }
        }
        this.groupedBackendModules = this.groupService.groupOptions(s.GroupedBackendModules, (v) => v.GroupType,
          this.groupService.compareFields(v => v.OrderKey, v => v.GroupType, v => v.DisplayName)
        );
      } else {
        this.groupedBackendModules = [];
      }
      if (this.backend === undefined) {
        this.setBackend(this.defaultBackend);
      } else {
        this.updateAdvancedOptionList();
      }
      this.reparseUri();
    });
  }

  setUri(uri: string, forceReparse?: boolean) {
    if (this.uri !== uri || forceReparse) {
      this.uri = uri;
      this.reparseUri();
    }
  }

  setBackend(b: ModuleDescription | undefined): void {
    this.backend = b;
    this.updateAdvancedOptionList();
    this.loadEditor();
  }

  buildUri(): Observable<string> {
    if (this.editorComponent) {
      return this.editorComponent.instance.buildUri(this.advancedOptions);
    }
    return EMPTY;
  }

  private updateAdvancedOptionList(): void {
    let opts = structuredClone(this.backend?.Options || []);
    for (let o of opts) {
      o.Category = this.backend?.DisplayName;
    }
    if (this.systemInfo != null) {
      for (let m of this.systemInfo.ConnectionModules) {
        let t = structuredClone(m.Options) || [];
        for (let v of t) {
          v.Category = m.DisplayName;
        }
        opts.push(...t);
      }
    }
    this.advancedOptionList = opts;
  }

  private reparseUri(): void {
    if (this.systemInfo == null) {
      return;
    }
    if (this.uri == null || this.uri.trim().length === 0) {
      this.setBackend(this.defaultBackend);
      this.commonData = {};
      this.advancedOptions = [];
      return;
    }
    // Set backend to load correct editor
    const uriBackend = this.editUriService.getBackend(this.uri, this.systemInfo.GroupedBackendModules ?? []);
    if (uriBackend != null) {
      this.setBackend(uriBackend);
    } else {
      this.setBackend(this.defaultBackend);
    }

    let parser = undefined;
    if (this.editorComponent != null) {
      parser = (data: CommonBackendData, parts: Map<string, string>) => this.editorComponent!.instance.parseUriParts(data, parts);
    }
    let res = this.editUriService.parseUri(this.uri, this.systemInfo?.GroupedBackendModules, parser);
    this.commonData = res.data;
    if (this.editorComponent != null) {
      // Have to re-trigger change detection, because it does not work automatically
      this.editorComponent.setInput('commonData', this.commonData);
    }
    this.advancedOptions = res.advanced;
  }

  private loadEditor(): void {
    const viewContainerRef = this.editorHost.viewContainerRef;
    viewContainerRef.clear();
    this.editorComponent = undefined;

    if (this.backend) {
      const editor = this.editUriService.getEditorType(this.backend.Key);
      if (editor) {
        // Have to create new injectors for every backend type, otherwise it will not update
        let editorInjector = this.editUriService.createEditorInjector(this.backend, this.injector);
        this.editorComponent = viewContainerRef.createComponent<BackendEditorComponent>(editor, {
          injector: editorInjector
        });
        if (this.commonData == null) {
          this.commonData = {};
        }
        this.editorComponent.setInput('commonData', this.commonData);
      }
    }
  }

  trackGroup(index: number, item: { key: string, value: ModuleDescription[] }): string {
    return item.key;
  }

  trackBackend(index: number, item: ModuleDescription): string {
    return item.Key;
  }

  testConnection(): void {
    this.buildUri().subscribe(uri => this.connectionTester.performConnectionTest(uri, this.advancedOptions, () => this.buildUri(),
      this.editorComponent == null ? undefined : () => this.editorComponent!.instance.extraConnectionTests())
    );
  }
}
