import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from "@angular/common/http";
import { Injectable } from "@angular/core";
import { Router } from "@angular/router";
import { catchError, Observable, throwError } from "rxjs";
import { DialogService } from "../services/dialog.service";

@Injectable()
export class LoginInterceptor implements HttpInterceptor {
  login_url = '/login.html?v=2.0.0.7';

  constructor(private dialogService: DialogService, private router: Router) { }

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    return next.handle(req).pipe(catchError(e => this.handleError(e)));
  }

  private handleError(error: HttpErrorResponse) {
    if (error.status == 401) {
      this.dialogService.dismissAll();
      this.dialogService.accept('Not logged in', () => { this.router.navigateByUrl(this.login_url); });
    }
    return throwError(error);
  }
}
