import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class BrandingService {

  constructor() { }

  getAppName(): Observable<string> {
    return of('Duplicati');
  }
  getAppSubtitle(): Observable<string|null> {
    return of(null);
  }
  getAppLogoPath(): Observable<string> {
    return of('img/logo.png');
  }

}
