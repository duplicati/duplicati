import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientModule, HttpClientXsrfModule, HTTP_INTERCEPTORS } from '@angular/common/http';

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
import { EditorHostDirective } from './directives/editor-host.directive';
import { EditFileComponent } from './editors/edit-file/edit-file.component';
import { backendEditorProviders } from './editors';
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
    EditorHostDirective,
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
    ExportComponent
  ],
  imports: [
    BrowserModule,
    HttpClientModule,
    FormsModule,
    ReactiveFormsModule,
    MatTreeModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
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
    backendEditorProviders,
    backupCheckerProviders
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }
