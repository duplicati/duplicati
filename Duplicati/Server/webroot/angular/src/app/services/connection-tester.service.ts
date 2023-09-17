import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { booleanAttribute, inject } from '@angular/core';
import { Inject } from '@angular/core';
import { InjectionToken } from '@angular/core';
import { Injectable } from '@angular/core';
import { catchError, concatMap, defaultIfEmpty, defer, EMPTY, filter, first, switchAll, switchMap, tap, throwError } from 'rxjs';
import { distinct } from 'rxjs';
import { from } from 'rxjs';
import { map, Observable, of } from 'rxjs';
import { SystemInfo } from '../system-info/system-info';
import { SystemInfoService } from '../system-info/system-info.service';
import { DialogConfig } from './dialog-config';
import { DialogService } from './dialog.service';


export interface ConnectionCreator {
  builduri: () => Observable<string>;
  advancedOptions: string[];
  updateAdvancedOptions(): void;
  backendTester?: () => Observable<boolean>;
}


export interface ConnectionErrorHandler {
  // Return true if the error handler should be attempted
  canHandleError(err: any): boolean;
  // Can modify the arguments and return retrySource to rebuild the uri, return EMPTY to try next handler
  // Return a value if the connection was successful
  handleError(err: any, retrySource: Observable<void>, data: { currentUri: string, creator: ConnectionCreator, setTesting: (v: boolean) => void }): Observable<void>;
}

export const CONNECTION_ERROR_HANDLERS = new InjectionToken<(() => ConnectionErrorHandler)[]>('Connection error handlers', {
  providedIn: 'root', factory: () => []
});

@Injectable({
  providedIn: 'root'
})
export class ConnectionTester {

  constructor(private dialog: DialogService,
    private client: HttpClient,
    private systemInfo: SystemInfoService,
    @Inject(CONNECTION_ERROR_HANDLERS) private connectionErrorHandlers: (() => ConnectionErrorHandler)[]) { }

  private testing = false;

  isTesting(): boolean {
    return this.testing;
  }

  performConnectionTest(connectionCreator: ConnectionCreator): Observable<void> {
    let hasTriedCreate = false;
    let hasTriedCert = false;
    let hasTriedMozroots = false;
    let hasTriedHostkey = false;
    let testingDialog: DialogConfig | undefined;

    const data = {
      currentUri: '',
      setTesting: (v: boolean) => this.testing = v,
      creator: connectionCreator
    };

    let errorHandlers: ConnectionErrorHandler[] = this.connectionErrorHandlers.map(f => f());

    // Handle error, can return source to retry or empty to stop
    let handleError = (err: HttpErrorResponse, source: Observable<void>) => {
      this.testing = false;
      if (testingDialog != null && testingDialog.dismiss != null) {
        testingDialog.dismiss();
      }

      return from(errorHandlers).pipe(
        filter(h => h.canHandleError(err)),
        defaultIfEmpty(null),
        concatMap(h => {
          if (h != null) {
            return h.handleError(err, source, data);
          } else {
            this.dialog.connectionError($localize`Failed to connect: `, err);
            return EMPTY;
          }
        })
      );
    };

    let testConnection = defer(() => {
      this.testing = true;
      testingDialog?.dismiss();
      return connectionCreator.builduri().pipe(
        switchMap(newUri => {
          data.currentUri = newUri;
          return this.dialog.dialogObservable($localize`Testing ...`, $localize`Testing connection ...`, []).pipe(
            filter(v => v.event == 'show'),
            switchMap(v => {
              testingDialog = v.config;
              return this.client.post<void>('/remoteoperation/test', data.currentUri);
            }));
        }));
    }).pipe(
      catchError(handleError),
      switchMap(() => {
        this.testing = false;
        testingDialog?.dismiss();
        return connectionCreator.backendTester != null ? connectionCreator.backendTester() : of(true);
      }),
      defaultIfEmpty(false),
      map(v => {
        if (v) {
          this.dialog.dialog($localize`Success`, $localize`Connection worked`);
        } else {
          this.dialog.dialog($localize`Error`, $localize`Connection failed`);
        }
      }));

    return testConnection;
  }
}
