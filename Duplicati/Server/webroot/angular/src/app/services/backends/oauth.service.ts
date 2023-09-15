import { HttpClient } from '@angular/common/http';
import { Inject, Injectable, InjectionToken } from '@angular/core';
import { SafeUrl } from '@angular/platform-browser';
import { filter, single, tap } from 'rxjs';
import { map, range, switchMap, take, takeWhile, throwError, timer } from 'rxjs';
import { Observable, of } from 'rxjs';

export const OAUTH_SERVICE_LINK = new InjectionToken<string>('Oauth service link', { providedIn: 'root', factory: () => 'https://duplicati-oauth-handler.appspot.com/' });

@Injectable({
  providedIn: 'root'
})
export class OauthService {

  constructor(@Inject(OAUTH_SERVICE_LINK) private oauthServiceLink: string,
    private client: HttpClient) { }

  generateToken(key: string): { token: string, startLink: string } {
    const token = Math.random().toString(36).substr(2) + Math.random().toString(36).substr(2);
    const startLink = this.oauthServiceLink + `?type=${key}&token=${token}`;
    return {
      token: token,
      startLink: startLink
    };
  }

  showTokenWindow(token: string, startLink: string): Observable<string> {
    const w = 450;
    const h = 600;

    const url = startLink;
    let ft = token;
    const left = (screen.width / 2) - (w / 2);
    const top = (screen.height / 2) - (h / 2);
    let wnd = window.open(url, '_blank', `height=${h},width=${w},menubar=0,status=0,titlebar=0,toolbar=0,left=${left},top=${top}`);

    let recheck = (): Observable<{ authid?: string }> => {
      return this.client.jsonp<{ authid?: string }>(`${this.oauthServiceLink}fetch?token=${encodeURIComponent(ft)}`, 'callback');
    }
    return timer(6000, 3000).pipe(
      // Max 100 attempts
      take(100),
      // Trigger recheck when timer triggers
      switchMap(v => recheck()),
      filter(v => v.authid != null),
      // Complete when authid is returned or attempts are reached
      take(1),
      // Close window when done
      tap({
        complete: () => {
          if (wnd != null) {
            wnd.close();
          }
        }
      }),
      // Assert that a value was produced
      single(),
      map(v => v.authid!)
    );
  }
}
