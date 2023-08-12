import { Component, EventEmitter, Input, Output, ViewChild } from '@angular/core';
import { EditorHostDirective } from '../../directives/editor-host.directive';
import { BackendEditorComponent } from '../../editors/backend-editor.component';
import { ConvertService } from '../../services/convert.service';
import { EditUriService } from '../../services/edit-uri.service';
import { GroupedOptions, GroupedOptionService } from '../../services/grouped-option.service';
import { ParserService } from '../../services/parser.service';
import { CommandLineArgument, GroupedModuleDescription, ModuleDescription } from '../../system-info/system-info';
import { SystemInfoService } from '../../system-info/system-info.service';


@Component({
  selector: 'app-backup-edit-uri',
  templateUrl: './backup-edit-uri.component.html',
  styleUrls: ['./backup-edit-uri.component.less']
})
export class BackupEditUriComponent {
  @Input({ required: true }) uri!: string;
  @Output() uriChange = new EventEmitter<string>();

  backend?: ModuleDescription;
  defaultBackend?: ModuleDescription;
  editorComponent?: BackendEditorComponent;
  groupedBackendModules: GroupedOptions<ModuleDescription> = [];
  testing: boolean = false;
  showAdvanced: boolean = false;
  showAdvancedTextArea: boolean = false;
  advancedOptions: string[] = [];
  advancedOptionList: CommandLineArgument[] = [];

  @ViewChild(EditorHostDirective, { static: true }) editorHost!: EditorHostDirective;

  constructor(public parser: ParserService,
    public convert: ConvertService,
    private systemInfoService: SystemInfoService,
    private editUriService: EditUriService,
    private groupService: GroupedOptionService) { }

  ngOnInit() {
    this.systemInfoService.getState().subscribe(s => {
      this.defaultBackend = undefined;
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
      }
    });
  }

  setBackend(b: ModuleDescription | undefined): void {
    this.backend = b;
    this.loadEditor();
  }

  private loadEditor(): void {
    const viewContainerRef = this.editorHost.viewContainerRef;
    viewContainerRef.clear();
    this.editorComponent = undefined;

    if (this.backend) {
      const editor = this.editUriService.getEditorType(this.backend?.Key);
      if (editor) {
        this.editorComponent = viewContainerRef.createComponent<BackendEditorComponent>(editor).instance;
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

  }
}
