import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { AboutComponent } from './about/about.component';
import { AddWizardComponent } from './add-wizard/add-wizard.component';
import { EditBackupComponent } from './edit-backup/edit-backup.component';
import { HomeComponent } from './home/home.component';
import { ServerLogComponent } from './server-log/server-log.component';
import { SettingsComponent } from './settings/settings.component';

const routes: Routes = [
  { path: '', component: HomeComponent, pathMatch: 'full' },
  { path: 'settings', component: SettingsComponent },
  { path: 'about', component: AboutComponent },
  { path: 'log', component: ServerLogComponent },
  { path: 'addstart', component: AddWizardComponent },
  { path: 'add', component: EditBackupComponent },
  { path: 'edit/:id', component: EditBackupComponent }
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
