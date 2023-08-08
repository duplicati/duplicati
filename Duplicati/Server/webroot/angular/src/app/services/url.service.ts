import { Inject, Injectable } from '@angular/core';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { CookieService } from 'ngx-cookie-service';
import { API_URL } from '../interceptors/api-url-interceptor';

@Injectable({
  providedIn: 'root'
})
export class UrlService {
  constructor(
    @Inject(API_URL) private apiUrl: string,
    private cookies: CookieService) { }

  private getEncodedXsrfToken(): string {
    return encodeURIComponent(this.cookies.get('xsrf-token'));
  }

  getExportUrl(backupid: string, passphrase: string | undefined, exportPasswords: boolean): string {
    let rurl = `${this.apiUrl}/backup/${backupid}/export?x-xsrf-token=${this.getEncodedXsrfToken()}&export-passwords=${encodeURIComponent(exportPasswords)}`;
    if ((passphrase || '').trim().length > 0) {
      rurl += `&passphrase=${encodeURIComponent(passphrase as string)}`;
    }
    return rurl;
  }
  getBugreportUrl(reportid: string): string {
    return `${this.apiUrl}/bugreport/${encodeURIComponent(reportid)}?x-xsrf-token=${this.getEncodedXsrfToken()}`;
  }

}
