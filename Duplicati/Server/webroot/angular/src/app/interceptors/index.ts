import { HTTP_INTERCEPTORS } from "@angular/common/http";
import { APIUrlInterceptor } from "./api-url-interceptor";
import { ErrorHandlerInterceptor } from "./error-handler-interceptor";
import { LocaleInterceptor } from "./locale-interceptor";
import { LoginInterceptor } from "./login-interceptor";

export const httpInterceptorProviders = [
  //{ provide: HTTP_INTERCEPTORS, useClass: ErrorHandlerInterceptor, multi: true },
  { provide: HTTP_INTERCEPTORS, useClass: APIUrlInterceptor, multi: true },
  { provide: HTTP_INTERCEPTORS, useClass: LocaleInterceptor, multi: true },
  { provide: HTTP_INTERCEPTORS, useClass: LoginInterceptor, multi: true }
];
