import { LOCALE_ID, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientJsonpModule, HttpClientModule, HttpClientXsrfModule, HTTP_INTERCEPTORS } from '@angular/common/http';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { HeaderComponent } from './header/header.component';
import { FooterComponent } from './footer/footer.component';
import { StateComponent } from './state/state.component';
import { ExternalLinkComponent } from './external-link/external-link.component';
import { MainMenuComponent } from './main-menu/main-menu.component';
import { HomeComponent } from './home/home.component';
import { BackupTaskComponent } from './backup-task/backup-task.component';
import { DialogComponent } from './dialog/dialog.component';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { ConnectionLostComponent } from './connection-lost/connection-lost.component';
import { CookieService } from 'ngx-cookie-service';
import { httpInterceptorProviders } from './interceptors';
import { SettingsComponent } from './settings/settings.component';
import { AboutComponent } from './about/about.component';
import { NotificationAreaComponent } from './notification-area/notification-area.component';
import { ServerLogComponent } from './server-log/server-log.component';
import { LogEntryComponent } from './server-log/log-entry.component';
import { AdvancedOptionsEditorComponent } from './advanced-options-editor/advanced-options-editor.component';
import { StringArrayTextDirective } from './directives/string-array-text.directive';
import { AddWizardComponent } from './add-wizard/add-wizard.component';
import { EditBackupComponent } from './edit-backup/edit-backup.component';
import { BackupGeneralSettingsComponent } from './edit-backup/backup-general-settings/backup-general-settings.component';
import { BackupDestinationSettingsComponent } from './edit-backup/backup-destination-settings/backup-destination-settings.component';
import { BackupEditUriComponent } from './edit-backup/backup-edit-uri/backup-edit-uri.component';
import { ContextMenuComponent } from './context-menu/context-menu.component';
import { DynamicHostDirective } from './directives/dynamic-host.directive';
import { DestinationFolderPickerComponent } from './destination-folder-picker/destination-folder-picker.component';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { MatTreeModule } from '@angular/material/tree';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { SourceFolderPickerComponent } from './source-folder-picker/source-folder-picker.component';
import { BackupSourceSettingsComponent } from './edit-backup/backup-source-settings/backup-source-settings.component';
import { BackupFilterComponent } from './edit-backup/backup-filter/backup-filter.component';
import { BackupFilterListComponent } from './edit-backup/backup-filter/backup-filter-list.component';
import { BackupScheduleComponent } from './edit-backup/backup-schedule/backup-schedule.component';
import { BackupOptionsComponent } from './edit-backup/backup-options/backup-options.component';
import { WidgetsModule } from './widgets/widgets.module';
import { backupCheckerProviders } from './backup-checks';
import { LocalDatabaseComponent } from './local-database/local-database.component';
import { DeleteComponent } from './delete/delete.component';
import { UpdateChangelogComponent } from './update-changelog/update-changelog.component';
import { ImportComponent } from './import/import.component';
import { BackupLogComponent } from './backup-log/backup-log.component';
import { ResultEntryComponent } from './backup-log/result-entry.component';
import { BackupResultComponent } from './backup-result/backup-result.component';
import { MessageListComponent } from './backup-result/message-list.component';
import { TestPhaseComponent } from './backup-result/phases/test-phase.component';
import { CompactPhaseComponent } from './backup-result/phases/compact-phase.component';
import { PhaseBaseComponent } from './backup-result/phases/phase-base.component';
import { RepairPhaseComponent } from './backup-result/phases/repair-phase.component';
import { DeletePhaseComponent } from './backup-result/phases/delete-phase.component';
import { PurgePhaseComponent } from './backup-result/phases/purge-phase.component';
import { ExportComponent } from './export/export.component';
import { RestoreComponent } from './restore/restore.component';
import { RestoreWizardComponent } from './restore-wizard/restore-wizard.component';
import { RestoreDirectComponent } from './restore-direct/restore-direct.component';
import { RestoreFilePickerComponent } from './restore-file-picker/restore-file-picker.component';
import { WaitAreaComponent } from './wait-area/wait-area.component';
import { RestoreSelectFilesComponent } from './restore/restore-select-files.component';
import { RestoreLocationComponent } from './restore/restore-location.component';
import { HighlightPipe } from './pipes/highlight.pipe';
import { CommandlineComponent } from './commandline/commandline.component';
import { CopyClipboardButtonsComponent } from './dialog-templates/copy-clipboard-buttons/copy-clipboard-buttons.component';
import { CaptchaComponent } from './dialog-templates/captcha/captcha.component';
import { ThrottleComponent } from './dialog-templates/throttle/throttle.component';
import { PauseComponent } from './dialog-templates/pause/pause.component';
import { DynamicContentComponent } from './dynamic-content/dynamic-content.component';
import { ClipboardModule } from 'ngx-clipboard';
import { BackendEditorsModule } from './backend-editors/backend-editors.module';
import { EditFileComponent } from './edit-file/edit-file.component';
import { BACKEND_EDITORS } from './backend-editor';
import { ServerSettingsService } from './services/server-settings.service';

@NgModule({
  declarations: [
    AppComponent,
    HeaderComponent,
    FooterComponent,
    StateComponent,
    ExternalLinkComponent,
    MainMenuComponent,
    HomeComponent,
    BackupTaskComponent,
    DialogComponent,
    ConnectionLostComponent,
    SettingsComponent,
    AboutComponent,
    NotificationAreaComponent,
    ServerLogComponent,
    LogEntryComponent,
    AdvancedOptionsEditorComponent,
    StringArrayTextDirective,
    AddWizardComponent,
    EditBackupComponent,
    BackupGeneralSettingsComponent,
    BackupDestinationSettingsComponent,
    BackupEditUriComponent,
    ContextMenuComponent,
    DynamicHostDirective,
    EditFileComponent,
    DestinationFolderPickerComponent,
    SourceFolderPickerComponent,
    BackupSourceSettingsComponent,
    BackupFilterComponent,
    BackupFilterListComponent,
    BackupScheduleComponent,
    BackupOptionsComponent,
    LocalDatabaseComponent,
    DeleteComponent,
    UpdateChangelogComponent,
    ImportComponent,
    BackupLogComponent,
    ResultEntryComponent,
    BackupResultComponent,
    MessageListComponent,
    TestPhaseComponent,
    CompactPhaseComponent,
    PhaseBaseComponent,
    RepairPhaseComponent,
    DeletePhaseComponent,
    PurgePhaseComponent,
    ExportComponent,
    RestoreComponent,
    RestoreWizardComponent,
    RestoreDirectComponent,
    RestoreFilePickerComponent,
    WaitAreaComponent,
    RestoreSelectFilesComponent,
    RestoreLocationComponent,
    HighlightPipe,
    CommandlineComponent,
    CopyClipboardButtonsComponent,
    CaptchaComponent,
    ThrottleComponent,
    PauseComponent,
    DynamicContentComponent
  ],
  imports: [
    BrowserModule,
    HttpClientModule,
    HttpClientJsonpModule,
    FormsModule,
    ReactiveFormsModule,
    MatTreeModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    ClipboardModule,
    BackendEditorsModule,
    HttpClientXsrfModule.withOptions({
      headerName: 'X-XSRF-Token',
      cookieName: 'xsrf-token'
    }),
    BrowserAnimationsModule,
    WidgetsModule,
    AppRoutingModule,
  ],
  providers: [
    CookieService,
    httpInterceptorProviders,
    { provide: BACKEND_EDITORS, useValue: { key: 'file', type: EditFileComponent }, multi: true },
    {
      provide: LOCALE_ID,
      useFactory: (settings: ServerSettingsService) => settings.getUILanguage(),
      deps: [ServerSettingsService]
    },
    backupCheckerProviders
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }
