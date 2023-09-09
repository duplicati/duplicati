import { Component } from '@angular/core';
import { Subscription } from 'rxjs';
import { BrandingService } from '../services/branding.service';
import { DialogService } from '../services/dialog.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { UpdateService } from '../services/update.service';
import { SystemInfo } from '../system-info/system-info';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-update-changelog',
  templateUrl: './update-changelog.component.html',
  styleUrls: ['./update-changelog.component.less']
})
export class UpdateChangelogComponent {
  systemInfo?: SystemInfo;
  version?: string;
  changelog?: string;
  serverstate?: ServerStatus;

  private subscription?: Subscription;

  constructor(public brandingService: BrandingService,
    private systemInfoService: SystemInfoService,
    private dialog: DialogService,
    private serverStatusService: ServerStatusService,
    private updateService: UpdateService) { }

  ngOnInit() {
    this.subscription = this.systemInfoService.getState().subscribe(info => this.systemInfo = info);
    this.subscription.add(this.serverStatusService.getStatus().subscribe(status => this.serverstate = status));
    this.reloadChangeLog();
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  reloadChangeLog() {
    this.updateService.getUpdateChangelog().subscribe(update => {
      this.version = update.Version;
      this.changelog = update.Changelog;
    });
  }

  doInstall() {
    this.updateService.startUpdateDownload().subscribe({ error: this.dialog.connectionError('Install failed: ') });
  }

  doActivate() {
    this.updateService.startUpdateActivate().subscribe({ error: this.dialog.connectionError('Activate failed: ') });
  }

  doCheck() {
    this.updateService.checkForUpdates().subscribe(() => this.reloadChangeLog(),
      this.dialog.connectionError('Check failed: '));
  }
}
