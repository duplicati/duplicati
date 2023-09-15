import { Component, EventEmitter, Output } from '@angular/core';
import { BrandingService } from '../services/branding.service';
import { SystemInfoService } from '../system-info/system-info.service';
import { SystemInfo } from '../system-info/system-info';
import { Observable } from 'rxjs';
import { ServerStatus } from '../services/server-status';
import { DialogService } from '../services/dialog.service';
import { ServerStatusService } from '../services/server-status.service';
import { Subscription } from 'rxjs';
import { PauseComponent } from '../dialog-templates/pause/pause.component';
import { ThrottleComponent } from '../dialog-templates/throttle/throttle.component';
import { ServerSettingsService } from '../services/server-settings.service';

@Component({
  selector: 'app-header',
  templateUrl: './header.component.html',
  styleUrls: ['./header.component.less']
})
export class HeaderComponent {
  @Output() toggleMenu = new EventEmitter<void>();

  public systemInfo?: SystemInfo;
  public get state(): ServerStatus {
    return this.serverStatus.status;
  }
  public throttle_active: boolean = false;

  private subscription?: Subscription;

  constructor(
    public brandingService: BrandingService,
    private dialog: DialogService,
    private serverStatus: ServerStatusService,
    private serverSettings: ServerSettingsService,
    private systemInfoService: SystemInfoService) { }

  ngOnInit(): void {
    this.subscription = this.systemInfoService.getState().subscribe(v => { this.systemInfo = v; });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  public pauseOptions(): void {
    if (this.state.programState != 'Running') {
      this.serverStatus.resume();
    } else {
      this.dialog.htmlDialog('Pause options', PauseComponent, ['OK', 'Cancel'],
        (index, text, cur) => {
          if (index == 0 && cur != null && cur.data?.time != null) {
            let time = cur.data.time as string;
            this.serverStatus.pause(time == 'infinite' ? '' : time);
          }
        }
      );
    }
  }

  public throttleOptions(): void {
    this.dialog.htmlDialog('Throttle settings', ThrottleComponent, ['OK', 'Cancel'],
      (index, text, cur) => {
        if (index == 0 && cur.data != null && cur.data.uploadspeed != null && cur.data.downloadspeed != null) {
          let patchdata = {
            'max-download-speed': cur.data.downloadthrottleenabled ? cur.data.downloadspeed : '',
            'max-upload-speed': cur.data.uploadthrottleenabled ? cur.data.uploadspeed : ''
          };
          this.serverSettings.updateSettings(patchdata).subscribe(
            () => this.throttle_active = cur.data.downloadthrottleenabled || cur.data.uploadthrottleenabled,
            this.dialog.connectionError('Failed to connect: ')
          );
        }
      }
    );
  }

  onClickMenu(event: Event) {
    event.stopPropagation();
    event.preventDefault();
    this.toggleMenu.emit();
  }
}
