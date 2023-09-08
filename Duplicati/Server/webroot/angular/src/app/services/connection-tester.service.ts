import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { DialogConfig } from './dialog-config';
import { DialogService } from './dialog.service';

@Injectable({
  providedIn: 'root'
})
export class ConnectionTester {

  constructor(private dialog: DialogService,
    private client: HttpClient) { }

  private testing = false;

  isTesting(): boolean {
    return this.testing;
  }

  performConnectionTest(uri: string, advancedOptions: string[], builduri: () => string | null, backendTester?: () => boolean): void {
    let hasTriedCreate = false;
    let hasTriedCert = false;
    let hasTriedMozroots = false;
    let hasTriedHostkey = false;
    let dlg: DialogConfig | undefined;

    let testConnection = () => {
      this.testing = true;
      if (dlg != null && dlg.dismiss != null) {
        dlg.dismiss();
      }

      dlg = this.dialog.dialog('Testing ...', 'Testing connection ...', [], undefined, () => {
        this.client.post('/remoteoperation/test', uri).subscribe(() => {
          this.testing = false;
          if (dlg?.dismiss) {
            dlg.dismiss();
          }
          if (backendTester == null || backendTester()) {
            this.dialog.dialog('Success', 'Connection worked');
          }
        }, handleError);
      });
    };

    let createFolder = () => {
      hasTriedCreate = true;
      this.testing = true;
      this.client.post<void>('/remoteoperation/create', uri).subscribe(testConnection, handleError);
    };

    const certOptionName = '--accept-specified-ssl-hash=';
    let appendApprovedCert = (hash: string) => {
      hasTriedCert = true;
      let idx = advancedOptions.findIndex(opt => opt.startsWith(certOptionName));
      if (idx >= 0) {
        let certOption = advancedOptions[idx];
        let certs = certOption.substr(certOptionName.length).split(',');
        if (certs.includes(hash)) {
          // Already has cert
          return;
        } else {
          advancedOptions[idx] = certOption + ',' + hash;
        }
      } else {
        advancedOptions.push(certOptionName + hash);
      }
    };

    let askApproveCert = (hash: string) => {
      this.dialog.dialog('Trust server certificate?',
        `The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: ${hash}?`,
        ['No', 'Yes'],
        (idx) => {
          if (idx === 1) {
            appendApprovedCert(hash);
            const newUri = builduri();
            if (newUri != null) {
              uri = newUri;
              testConnection();
            }
          }
        });
    };

    let hasCertApproved = (hash: string): boolean => {
      let certOption = advancedOptions.find(opt => opt.startsWith(certOptionName));
      if (certOption != null) {
        let certs = certOption.substr(certOptionName.length).split(',');
        return certs.includes(hash);
      }
      return false;
    };

    let handleError = (err: HttpErrorResponse) => {
      this.testing = false;
      if (dlg != null && dlg.dismiss != null) {
        dlg.dismiss();
      }

      const message = err.statusText;
      // TODO: Implement common error resolutions
      if (!hasTriedCreate && message == 'missing-folder') {

      } else if (!hasTriedCert && message.startsWith('incorrect-cert:')) {

      } else if (!hasTriedHostkey && message.startsWith('incorrect-host-key:')) {

      } else {
        this.dialog.connectionError('Failed to connect: ', err);
      }
    };

    testConnection();
  }
}
