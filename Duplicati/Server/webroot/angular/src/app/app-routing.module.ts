import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AboutComponent } from './about/about.component';
import { AddWizardComponent } from './add-wizard/add-wizard.component';
import { BackupLogComponent } from './backup-log/backup-log.component';
import { DeleteComponent } from './delete/delete.component';
import { EditBackupComponent } from './edit-backup/edit-backup.component';
import { ExportComponent } from './export/export.component';
import { HomeComponent } from './home/home.component';
import { ImportComponent } from './import/import.component';
import { LocalDatabaseComponent } from './local-database/local-database.component';
import { RestoreDirectComponent } from './restore-direct/restore-direct.component';
import { RestoreWizardComponent } from './restore-wizard/restore-wizard.component';
import { RestoreComponent } from './restore/restore.component';
import { ServerLogComponent } from './server-log/server-log.component';
import { SettingsComponent } from './settings/settings.component';
import { UpdateChangelogComponent } from './update-changelog/update-changelog.component';

const routes: Routes = [
  { path: '', component: HomeComponent, pathMatch: 'full' },
  { path: 'settings', component: SettingsComponent },
  { path: 'about', component: AboutComponent },
  { path: 'log', component: ServerLogComponent },
  { path: 'log/:backupId', component: BackupLogComponent },
  { path: 'addstart', component: AddWizardComponent },
  { path: 'add', component: EditBackupComponent },
  { path: 'add-import', component: EditBackupComponent, data: { import: true } },
  { path: 'import', component: ImportComponent },
  { path: 'export/:backupId', component: ExportComponent },
  { path: 'edit/:backupId', component: EditBackupComponent },
  { path: 'restorestart', component: RestoreWizardComponent },
  { path: 'restore-import', component: ImportComponent, data: { restoremode: true } },
  { path: 'restoredirect', component: RestoreDirectComponent },
  { path: 'restoredirect-import', component: RestoreDirectComponent, data: { import: true } },
  { path: 'restore/:backupId', component: RestoreComponent },
  { path: 'localdb/:backupId', component: LocalDatabaseComponent },
  { path: 'delete/:backupId', component: DeleteComponent },
  { path: 'updatechangelog', component: UpdateChangelogComponent }
];

@NgModule({
  imports: [RouterModule.forRoot(routes, { bindToComponentInputs: true })],
  exports: [RouterModule]
})
export class AppRoutingModule { }
