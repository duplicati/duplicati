import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AzureComponent } from './azure/azure.component';
import { B2Component } from './b2/b2.component';
import { CosComponent } from './cos/cos.component';
import { E2Component } from './e2/e2.component';
import { GcsComponent } from './gcs/gcs.component';
import { OauthComponent } from './oauth/oauth.component';
import { GenericComponent } from './generic/generic.component';
import { JottacloudComponent } from './jottacloud/jottacloud.component';
import { MegaComponent } from './mega/mega.component';
import { MsgroupComponent } from './msgroup/msgroup.component';
import { OpenstackComponent } from './openstack/openstack.component';
import { RcloneComponent } from './rclone/rclone.component';
import { S3Component } from './s3/s3.component';
import { SharepointComponent } from './sharepoint/sharepoint.component';
import { SiaComponent } from './sia/sia.component';
import { StorjComponent } from './storj/storj.component';
import { TardigradeComponent } from './tardigrade/tardigrade.component';
import { backendEditorProviders, } from './index';



@NgModule({
  declarations: [
    AzureComponent,
    B2Component,
    CosComponent,
    E2Component,
    GcsComponent,
    OauthComponent,
    GenericComponent,
    JottacloudComponent,
    MegaComponent,
    MsgroupComponent,
    OpenstackComponent,
    RcloneComponent,
    S3Component,
    SharepointComponent,
    SiaComponent,
    StorjComponent,
    TardigradeComponent
  ],
  imports: [
    FormsModule,
    CommonModule
  ],
  providers: [
    backendEditorProviders
  ]
})
export class BackendEditorsModule { }
