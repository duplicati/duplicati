import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { CookieService } from 'ngx-cookie-service';
import { map, Observable } from 'rxjs';
import { DialogService } from './dialog.service';
import { ParserService } from './parser.service';

@Injectable({
  providedIn: 'root'
})
export class ServerSettingsService {


  saveThemeOnServer = false;
  activeTheme: string = 'default';
  savedTheme?: string;

  forceActualDate: boolean = false;
  throttleActive: boolean = false;

  constructor(private client: HttpClient,
    private cookies: CookieService,
    private parser: ParserService,
    private dialog: DialogService,
    private router: Router) { }

  loadCurrentTheme() {
    if (this.saveThemeOnServer) {
      this.client.get<any>('/uisettings/ngax').subscribe(data => {
        let theme = 'default';
        if (data != null && (data['theme'] || '').trim().length > 0) {
          theme = data['theme'];
        }

        let now = new Date();
        let exp = new Date(now.getFullYear() + 10, now.getMonth(), now.getDate());
        this.cookies.set('current-theme', theme, exp);
        this.savedTheme = this.activeTheme = theme;
      });
    }
  }

  getServerSettings(): Observable<Record<string, string>> {
    return this.client.get<Record<string, string>>('/serversettings');
  }

  initSettings() {
    this.getServerSettings().subscribe(data => {
      this.forceActualDate = this.parser.parseBoolString(data['--force-actual-date']);

      const ut = data['max-upload-speed'];
      const dt = data['max-download-speed'];
      this.throttleActive = (ut != null && ut.trim().length != 0) || (dt != null && dt.trim().length != 0);

      const firstpw = data['has-asked-for-password-protection'] as string | undefined;
      const haspw = data['server-passphrase'] as string | undefined;
      if (!firstpw && haspw == '') {
        this.dialog.dialog('First run setup',
          'If your machine is in a multi-user environment (i.e. the machine has more than one account), you need to set a password to prevent other users from accessing data on your account.\nDo you want to set a password now?',
          ['No, my machine has only a single account', 'Yes'],
          btn => {
            this.client.patch('/serversettings', { 'has-asked-for-password-protection': 'true' }, { headers: { 'Content-Type': 'application/json' } }).subscribe();
            if (btn === 1) {
              this.router.navigate(['/settings']);
            }
          });
      }
    });
  }

  getUILanguage(): string {
    return this.cookies.get('ui-locale') || '';
  }
  setUILanguage(uiLanguage: string) {
    if (uiLanguage.trim().length == 0) {
      this.cookies.delete('ui-locale');
      // TODO: Set language
      // setLanguage(this.systemInfo.BrowserLocale.Code.replace('-','_'));
    } else {
      let now = new Date();
      let exp = new Date(now.getFullYear() + 10, now.getMonth(), now.getDate());
      this.cookies.set('ui-locale', uiLanguage.replace('-', '_'), exp);
    }
  }

  updateTheme(theme: string) {
    // TODO: Update theme
  }

  checkForUpdates(): Observable<void> {
    return this.client.post<void>('/updates/check', '');
  }

  updateSettings(patchdata: Record<string, any>): Observable<void> {
    return this.client.patch<void>('/serversettings', patchdata, {
      headers: { 'Content-Type': 'application/json; charset=utf-8' }
    });
  }
}
