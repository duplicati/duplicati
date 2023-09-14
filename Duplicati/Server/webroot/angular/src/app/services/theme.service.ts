import { HttpClient } from '@angular/common/http';
import { InjectionToken } from '@angular/core';
import { Inject } from '@angular/core';
import { Injectable } from '@angular/core';
import { CookieService } from 'ngx-cookie-service';
import { StyleManagerService } from './style-manager.service';

export const SAVE_THEME_ON_SERVER = new InjectionToken<boolean>('save theme on server', { providedIn: 'root', factory: () => false });

@Injectable({
  providedIn: 'root'
})
export class ThemeService {

  readonly themeCookie = 'current-theme';
  readonly themeStyle = 'theme';

  private _activeTheme: string = 'default';
  get activeTheme(): string {
    return this._activeTheme;
  }
  set activeTheme(theme: string) {
    this._activeTheme = theme;
    this.styleManager.setStyle(this.themeStyle, `${theme}.css`);
  }
  savedTheme: string = 'default';

  constructor(@Inject(SAVE_THEME_ON_SERVER) private saveThemeOnServer = false,
    private client: HttpClient,
    private styleManager: StyleManagerService,
    private cookies: CookieService) { }

  loadCurrentTheme() {
    if (this.saveThemeOnServer) {
      this.client.get<any>('/uisettings/ngax').subscribe(data => {
        let theme = 'default';
        if (data != null && (data['theme'] || '').trim().length > 0) {
          theme = data['theme'];
        }

      });
    } else {
      this.savedTheme = this.activeTheme = this.cookies.get(this.themeCookie) || 'default';
    }
  }

  updateTheme(theme?: string) {
    if (theme == null) {
      theme = 'default';
    }

    if (this.saveThemeOnServer) {
      // Save it here to avoid flickering when the page changes
      this.savedTheme = this.activeTheme = theme;
      this.client.patch('/uisettings/ngax', { 'theme': theme }).subscribe(() => {
        this.setThemeCookie(theme!);
      });
    } else {
      this.setThemeCookie(theme);
    }
  }

  previewTheme(theme?: string) {
    if (!theme || theme.trim().length == 0) {
      this.activeTheme = this.savedTheme || 'default';
    } else {
      this.activeTheme = theme;
    }
  }

  private setThemeCookie(theme: string) {
    let now = new Date();
    let exp = new Date(now.getFullYear() + 10, now.getMonth(), now.getDate());
    this.cookies.set(this.themeCookie, theme, exp);
    this.savedTheme = this.activeTheme = theme;
  }

}
