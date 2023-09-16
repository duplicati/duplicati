import { StaticProvider } from "@angular/core";
import { AzureComponent } from "./azure/azure.component";
import { B2Component } from "./b2/b2.component";
import { BACKEND_EDITORS, DEFAULT_BACKEND_EDITOR } from "../backend-editor";
import { CosComponent } from "./cos/cos.component";
import { E2Component } from "./e2/e2.component";
import { GcsComponent } from "./gcs/gcs.component";
import { GenericComponent } from "./generic/generic.component";
import { JottacloudComponent } from "./jottacloud/jottacloud.component";
import { MegaComponent } from "./mega/mega.component";
import { MsgroupComponent } from "./msgroup/msgroup.component";
import { OauthComponent } from "./oauth/oauth.component";
import { OpenstackComponent } from "./openstack/openstack.component";
import { RcloneComponent } from "./rclone/rclone.component";
import { S3Component } from "./s3/s3.component";
import { SharepointComponent } from "./sharepoint/sharepoint.component";
import { SiaComponent } from "./sia/sia.component";
import { StorjComponent } from "./storj/storj.component";
import { TardigradeComponent } from "./tardigrade/tardigrade.component";

export const backendEditorProviders: StaticProvider[] = [
  { provide: DEFAULT_BACKEND_EDITOR, useValue: GenericComponent },
  { provide: BACKEND_EDITORS, useValue: { key: 's3', type: S3Component }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'googledrive', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'hubic', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'onedrive', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'onedrivev2', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'sharepoint', type: SharepointComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'msgroup', type: MsgroupComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'openstack', type: OpenstackComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'azure', type: AzureComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'gcs', type: GcsComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'b2', type: B2Component }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'mega', type: MegaComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'jottacloud', type: JottacloudComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'box', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'dropbox', type: OauthComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'sia', type: SiaComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'storj', type: StorjComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'tardigrade', type: TardigradeComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'rclone', type: RcloneComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'cos', type: CosComponent }, multi: true },
  { provide: BACKEND_EDITORS, useValue: { key: 'e2', type: E2Component }, multi: true },
];
