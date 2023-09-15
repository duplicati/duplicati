import { Component, Inject, InjectionToken, Input, SimpleChanges } from '@angular/core';
import { DialogConfig, DialogTemplate } from '../../services/dialog-config';
import { DialogService } from '../../services/dialog.service';
import { ParserService } from '../../services/parser.service';
import { ServerSettingsService } from '../../services/server-settings.service';

export const DEFAULT_THROTTLE = new InjectionToken<string>('Default throttle value', { providedIn: 'root', factory: () => '10MB' });

@Component({
  selector: 'app-dialog-throttle',
  templateUrl: './throttle.component.html',
  styleUrls: ['./throttle.component.less']
})
export class ThrottleComponent implements DialogTemplate {
  @Input() config: DialogConfig | undefined;


  get uploadthrottleenabled(): boolean {
    return this.config?.data?.uploadthrottleenabled || false;
  }
  set uploadthrottleenabled(v: boolean) {
    if (this.config) {
      if (this.config.data == null) {
        this.config.data = {};
      }
      this.config.data.uploadthrottleenabled = v;
    }
  }
  get downloadthrottleenabled(): boolean {
    return this.config?.data?.downloadthrottleenabled || false;
  }
  set downloadthrottleenabled(v: boolean) {
    if (this.config) {
      if (this.config.data == null) {
        this.config.data = {};
      }
      this.config.data.downloadthrottleenabled = v;
    }
  }
  get uploadspeed(): string {
    return this.config?.data?.uploadspeed || false;
  }
  set uploadspeed(v: string) {
    if (this.config) {
      if (this.config.data == null) {
        this.config.data = {};
      }
      this.config.data.uploadspeed = v;
    }
  }
  get downloadspeed(): string {
    return this.config?.data?.downloadspeed || false;
  }
  set downloadspeed(v: string) {
    if (this.config) {
      if (this.config.data == null) {
        this.config.data = {};
      }
      this.config.data.downloadspeed = v;
    }
  }
  speedMultipliers: ({ name: string, value: string })[] = [];

  constructor(@Inject(DEFAULT_THROTTLE) private defaultThrottle: string,
    private parser: ParserService,
    private dialog: DialogService,
    private settings: ServerSettingsService) { }

  ngOnChanges(changes: SimpleChanges) {
    if ('config' in changes) {
      this.updateSpeeds();
    }
  }

  updateSpeeds() {
    if (!this.config) {
      return;
    }
    this.speedMultipliers = this.parser.speedMultipliers;
    this.settings.getServerSettings().subscribe(s => {
      if (!this.config) {
        return;
      }
      let data: any = {};
      data.uploadspeed = s['max-upload-speed'];
      data.downloadspeed = s['max-download-speed'];
      data.uploadthrottleenabled = data.uploadspeed != '';
      data.downloadthrottleenabled = data.downloadspeed != '';

      // Nicer looking UI
      if (!data.uploadthrottleenabled) {
        data.uploadspeed = this.defaultThrottle;
      }
      if (!data.downloadthrottleenabled) {
        data.downloadspeed = this.defaultThrottle;
      }

      this.config.data = data;
    }, this.dialog.connectionError('Failed to connect: '));
  }
}
