import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { CookieService } from 'ngx-cookie-service';
import { tap } from 'rxjs';
import { Observable } from 'rxjs';
import { ADD_API_URL } from '../interceptors/api-url-interceptor';
import { UrlService } from './url.service';

@Injectable({
  providedIn: 'root'
})
export class LoginService {

  private authCookie = 'session-auth';

  constructor(private cookies: CookieService, private client: HttpClient, private url: UrlService) { }

  isLoggedIn(): boolean {
    return !!this.cookies.get(this.authCookie);
  }

  logOut(): Observable<void> {
    return this.client.get<void>(this.url.getLogoutUrl(), { context: new HttpContext().set(ADD_API_URL, false) }).pipe(
      tap(() => {
        this.cookies.delete(this.authCookie, '/');
    }));
  }
}
