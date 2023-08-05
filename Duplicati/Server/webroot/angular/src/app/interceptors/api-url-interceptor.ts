import { HttpContextToken, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Inject, Injectable, InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';

export const ADD_API_URL = new HttpContextToken(() => true);
export const API_URL = new InjectionToken<string>('api url', { providedIn: 'root', factory: () => '../api/v1' });

@Injectable()
export class APIUrlInterceptor implements HttpInterceptor {

  constructor(@Inject(API_URL) private api_url: string) { }

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    if (req.context.get(ADD_API_URL)) {
      req = req.clone({ url: this.api_url + req.url });
    }
    return next.handle(req);
  }
}
