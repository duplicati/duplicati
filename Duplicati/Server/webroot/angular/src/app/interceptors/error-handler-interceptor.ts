import { HttpContextToken, HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Inject, Injectable, InjectionToken } from '@angular/core';
import { catchError, Observable, throwError } from 'rxjs';
import { DialogService } from '../services/dialog.service';

@Injectable()
export class ErrorHandlerInterceptor implements HttpInterceptor {

  constructor(private dialogService: DialogService) { }

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(catchError(error => this.handleError(error)));
  }

  private handleError(error: HttpErrorResponse) {
    if (error.status === 0) {
      // A client-side or network error occurred. Handle it accordingly.
      console.error('An error occured:', error.error);
      this.dialogService.alert('An error occurred: ' + error.message);
    } else if (error.status == 400) {
      this.dialogService.alert(error.statusText);
    } else {
      // The backend returned an unsuccessful response code.
      // The response body may contain clues as to what went wrong.
      console.error(`Backend returned code ${error.status}, body was: `, error.error);
      this.dialogService.dialog('Error', `${error.status}: ${error.error}`);
    }
    return throwError(() => error);
  }
}
