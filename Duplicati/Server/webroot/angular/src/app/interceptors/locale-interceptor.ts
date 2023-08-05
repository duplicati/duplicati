import { HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CookieService } from 'ngx-cookie-service';

@Injectable()
export class LocaleInterceptor implements HttpInterceptor {

  constructor(private cookies: CookieService) { }

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    let localeReq = req;
    const locale = this.cookies.get('ui-locale');
    if (locale.length > 0) {
      localeReq = req.clone({
        headers: req.headers.set('X-UI-Language', locale)
      });
    }

    return next.handle(localeReq);
  }
}
