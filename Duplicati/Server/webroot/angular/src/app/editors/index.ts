import { AzureComponent } from "./azure/azure.component";
import { B2Component } from "./b2/b2.component";
import { BACKEND_EDITORS } from "./backend-editor";
import { CosComponent } from "./cos/cos.component";
import { E2Component } from "./e2/e2.component";
import { EditFileComponent } from "./edit-file/edit-file.component";
import { GcsComponent } from "./gcs/gcs.component";
import { OauthComponent } from "./oauth/oauth.component";

export const backendEditorProviders = [
  { provide: BACKEND_EDITORS, useValue: { key: 'file', type: EditFileComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'googledrive', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'hubic', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'onedrive', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'onedrivev2', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'azure', type: AzureComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'b2', type: B2Component }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'cos', type: CosComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'e2', type: E2Component }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'gcs', type: GcsComponent }, multi: true },
];
