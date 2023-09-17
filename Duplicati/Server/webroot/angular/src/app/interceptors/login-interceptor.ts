import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from "@angular/common/http";
import { Injectable } from "@angular/core";
import { Router } from "@angular/router";
import { catchError, Observable, throwError } from "rxjs";
import { DialogService } from "../services/dialog.service";
import { UrlService } from "../services/url.service";

@Injectable()
export class LoginInterceptor implements HttpInterceptor {
  constructor(private dialogService: DialogService, private router: Router, private url: UrlService) { }

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(catchError(e => this.handleError(e)));
  }

  private handleError(error: HttpErrorResponse) {
    if (error.status == 401) {
      this.dialogService.dismissAll();
      this.dialogService.accept($localize`Not logged in`, () => { location.href = this.url.getLoginUrl(); });
    }
    return throwError(error);
  }
}
