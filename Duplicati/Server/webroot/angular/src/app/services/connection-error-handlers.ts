import { HttpClient, HttpErrorResponse } from "@angular/common/http";
import { StaticProvider } from "@angular/core";
import { catchError, EMPTY, filter, first, map, Observable, switchMap } from "rxjs";
import { SystemInfoService } from "../system-info/system-info.service";
import { ConnectionCreator, ConnectionErrorHandler, CONNECTION_ERROR_HANDLERS } from "./connection-tester.service";
import { DialogService } from "./dialog.service";


class CreateFolderHandler implements ConnectionErrorHandler {
  hasTriedCreate = false;

  constructor(private client: HttpClient,
    private dialog: DialogService) { }

  canHandleError(err: any): boolean {
    return err instanceof HttpErrorResponse
      && (
        err.statusText == 'missing-folder' || err.error?.Error == 'missing-folder'
      );
  }
  handleError(err: any, retrySource: Observable<void>, data: { currentUri: string, creator: ConnectionCreator, setTesting: (v: boolean) => void }): Observable<void> {
    let createFolder = (): Observable<void> => {
      this.hasTriedCreate = true;
      data.setTesting(true);
      return this.client.post<void>('/remoteoperation/create', data.currentUri).pipe(
        catchError(err => {
          this.dialog.connectionError(err);
          return EMPTY;
        }));
    };

    const message = err.statusText;
    if (!this.hasTriedCreate && message == 'missing-folder') {
      let folder = '';
      return this.dialog.dialogObservable('Create folder?',
        `The folder ${folder} does not exist.\nCreate it now?`,
        ['No', 'Yes']).pipe(
          filter(v => v.event == 'button' && v.buttonIndex == 1),
          switchMap(() => createFolder()),
          // Retry
          switchMap(() => retrySource));
    }
    return EMPTY;
  }

}


class IncorrectCertificateHandler implements ConnectionErrorHandler {
  hasTriedMozroots = false;
  hasTriedCerts = false;

  constructor(private client: HttpClient,
    private dialog: DialogService,
    private systemInfo: SystemInfoService) { }

  canHandleError(err: any): boolean {
    return err instanceof HttpErrorResponse
      && (
        err.statusText.startsWith('incorrect-cert:') || err.error?.Error?.startsWith('incorrect-cert:')
      );
  }
  handleError(err: any, retrySource: Observable<void>, data: { currentUri: string, creator: ConnectionCreator, setTesting: (v: boolean) => void }): Observable<void> {
    const certOptionName = '--accept-specified-ssl-hash=';
    let appendApprovedCert = (hash: string) => {
      this.hasTriedCerts = true;
      let idx = data.creator.advancedOptions.findIndex(opt => opt.startsWith(certOptionName));
      if (idx >= 0) {
        let certOption = data.creator.advancedOptions[idx];
        let certs = certOption.substr(certOptionName.length).split(',');
        if (certs.includes(hash)) {
          // Already has cert
          return;
        } else {
          data.creator.advancedOptions[idx] = certOption + ',' + hash;
          data.creator.updateAdvancedOptions();
        }
      } else {
        data.creator.advancedOptions.push(certOptionName + hash);
        data.creator.updateAdvancedOptions();
      }
    };

    let hasCertApproved = (hash: string): boolean => {
      let certOption = data.creator.advancedOptions.find(opt => opt.startsWith(certOptionName));
      if (certOption != null) {
        let certs = certOption.substr(certOptionName.length).split(',');
        return certs.includes(hash);
      }
      return false;
    };

    let askApproveCert = (hash: string, source: Observable<void>) =>
      this.dialog.dialogObservable('Trust server certificate?',
        `The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: ${hash}?`,
        ['No', 'Yes']).pipe(
          filter(v => v.event == 'button' && v.buttonIndex == 1),
          switchMap(() => {
            appendApprovedCert(hash);
            return source;
          }));

    let tryMozroots = (hash: string, source: Observable<void>) => {
      this.hasTriedMozroots = true;
      let formData = new FormData();
      formData.set('mono-ssl-config', 'List');
      return this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/check-mono-ssl', formData).pipe(
        catchError(err => {
          this.dialog.connectionError(err);
          return EMPTY;
        }),
        switchMap(d => {
          if (d.Result['count'] == '0') {
            if (confirm('You appear to be running Mono with no SSL certificates loaded.\nDo you want to import the list of trusted certificates from Mozilla?')) {
              data.setTesting(true);
              let formData = new FormData();
              formData.set('mono-ssl-config', 'Install');
              return this.client.post<{ Status: string, Result: Record<string, string | null> }>('/webmodule/check-mono-ssl', formData).pipe(
                catchError(err => {
                  data.setTesting(false);
                  this.dialog.connectionError('Failed to import:', err);
                  return EMPTY;
                }),
                switchMap(d => {
                  data.setTesting(false);
                  if (d.Result['count'] == '0') {
                    this.dialog.dialog('Import failed', 'Import completed, but no certificates were found after the import');
                    return EMPTY;
                  } else {
                    return source;
                  }
                })
              );
            } else {
              return askApproveCert(hash, source);
            }
          } else {
            return askApproveCert(hash, source);
          }
        })
      );
    };
    let message: string = err?.statusText || '';
    if (!message.startsWith('incorrect-cert:')) {
      message = err.error?.Error || '';
    }
    if (!this.hasTriedCerts && message.startsWith('incorrect-cert:')) {
      let hash = message.substr('incorrect-cert:'.length);
      if (hasCertApproved(hash)) {
        this.dialog.connectionError(err);
        return EMPTY;
      }
      return this.systemInfo.getState().pipe(
        map(s => s.MonoVersion != null),
        first(),
        switchMap(hasMono => {
          if (hasMono && !this.hasTriedMozroots) {
            return tryMozroots(hash, retrySource);
          } else {
            return askApproveCert(hash, retrySource);
          }
        }));
    }
    return EMPTY;
  }
}

class IncorrectHostKeyHandler implements ConnectionErrorHandler {
  hasTriedHostkey = false;

  constructor(private dialog: DialogService) { }

  canHandleError(err: any): boolean {
    return err instanceof HttpErrorResponse
      && (
        err.statusText.startsWith('incorrect-host-key:') || err.error?.Error?.startsWith('incorrect-host-key:')
      );
  }
  handleError(err: any, retrySource: Observable<void>, data: { currentUri: string, creator: ConnectionCreator, setTesting: (v: boolean) => void }): Observable<void> {
    let errorMessage: string = err?.statusText || '';
    if (!errorMessage.startsWith('incorrect-cert:')) {
      errorMessage = err.error?.Error || '';
    }
    const re = /incorrect-host-key\s*:\s*"([^"]*)"(,\s*accepted-host-key\s*:\s*"([^"]*)")?/;
    const m = re.exec(errorMessage);
    let key = '';
    let prev = '';
    if (m != null) {
      key = m[1] || '';
      prev = m[3] || '';
    }

    if (key.trim().length == 0 || key == prev) {
      this.dialog.connectionError('Failed to connect: ', errorMessage);
      return EMPTY;
    } else {
      let message = prev.trim().length == 0 ?
        `No certificate was specified previously, please verify with the server administrator that the key is correct: ${key} \n\nDo you want to approve the reported host key?`
        : `The host key has changed, please check with the server administrator if this is correct, otherwise you could be the victim of a MAN-IN-THE-MIDDLE attack.\n\nDo you want to REPLACE your CURRENT host key "${prev}" with the REPORTED host key: ${key}?`;

      return this.dialog.dialogObservable('Trust host certificate?', message, ['No', 'Yes']).pipe(
        filter(v => v.event == 'button' && v.buttonIndex == 1),
        switchMap(() => {
          this.hasTriedHostkey = true;
          let idx = data.creator.advancedOptions.findIndex(opt => opt.startsWith('--ssh-fingerprint='));
          if (idx >= 0) {
            data.creator.advancedOptions.splice(idx, 1);
          }
          data.creator.advancedOptions.push('--ssh-fingerprint=' + key);
          data.creator.updateAdvancedOptions();
          return retrySource;
        })
      );
    }
  }
}

export const connectionErrorHandlerProviders: StaticProvider[] = [
  {
    provide: CONNECTION_ERROR_HANDLERS, multi: true, deps: [HttpClient, DialogService],
    useFactory: (client: HttpClient, dialog: DialogService) =>
      function () { return new CreateFolderHandler(client, dialog); }
  },
  {
    provide: CONNECTION_ERROR_HANDLERS, multi: true, deps: [HttpClient, DialogService, SystemInfoService],
    useFactory: (client: HttpClient, dialog: DialogService, systemInfo: SystemInfoService) =>
      function () { return new IncorrectCertificateHandler(client, dialog, systemInfo); }
  },
  {
    provide: CONNECTION_ERROR_HANDLERS, multi: true, deps: [DialogService],
    useFactory: (dialog: DialogService) =>
      function () { return new IncorrectHostKeyHandler(dialog); }
  }
];
