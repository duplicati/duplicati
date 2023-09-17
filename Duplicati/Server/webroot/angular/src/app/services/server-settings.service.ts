import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { CookieService } from 'ngx-cookie-service';
import { defaultIfEmpty } from 'rxjs';
import { of, take } from 'rxjs';
import { map, Observable } from 'rxjs';
import { SystemInfoService } from '../system-info/system-info.service';
import { DialogService } from './dialog.service';
import { ParserService } from './parser.service';

@Injectable({
  providedIn: 'root'
})
export class ServerSettingsService {

  forceActualDate: boolean = false;
  throttleActive: boolean = false;

  constructor(private client: HttpClient,
    private parser: ParserService,
    private dialog: DialogService,
    private systemInfo: SystemInfoService,
    private cookies: CookieService,
    private router: Router) { }

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
        this.dialog.dialog($localize`First run setup`,
          $localize`If your machine is in a multi-user environment (i.e. the machine has more than one account), you need to set a password to prevent other users from accessing data on your account.\nDo you want to set a password now?`,
          [$localize`No, my machine has only a single account`, $localize`Yes`],
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
    return this.cookies.get('ui-locale')?.replace('_','-') || '';
  }
  getLocale(): string {
    return this.getUILanguage() || navigator.language;
  }
  setUILanguage(uiLanguage: string) {
    if (uiLanguage.trim().length == 0) {
      this.cookies.delete('ui-locale');
    } else {
      let now = new Date();
      let exp = new Date(now.getFullYear() + 10, now.getMonth(), now.getDate());
      this.cookies.set('ui-locale', uiLanguage.replace('-', '_'), exp);
    }
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
