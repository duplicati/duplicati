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
import { API_URL } from './interceptors/api-url-interceptor';
import { SettingsComponent } from './settings/settings.component';
import { AboutComponent } from './about/about.component';
import { NotificationAreaComponent } from './notification-area/notification-area.component';
import { ServerLogComponent } from './server-log/server-log.component';
import { LogEntryComponent } from './server-log/log-entry.component';
import { AdvancedOptionsEditorComponent } from './advanced-options-editor/advanced-options-editor.component';
import { StringArrayTextDirective } from './directives/string-array-text.directive';


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
    StringArrayTextDirective
  ],
  imports: [
    BrowserModule,
    AppRoutingModule,
    HttpClientModule,
    FormsModule,
    ReactiveFormsModule,
    HttpClientXsrfModule.withOptions({
      headerName: 'X-XSRF-Token',
      cookieName: 'xsrf-token'
    })
  ],
  providers: [
    CookieService,
    httpInterceptorProviders
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }
