import { Inject, InjectionToken } from '@angular/core';
import { Component, Input, SimpleChanges } from '@angular/core';
import { DialogConfig, DialogTemplate } from '../../services/dialog-config';
import { ServerStatusService } from '../../services/server-status.service';

export const PAUSE_TIMES = new InjectionToken<string[]>('Pause times', {
  providedIn: 'root', factory: () => [
    '5m', '10m', '15m', '30m', '1h', '4h', '8h', '24h'
  ]
});

@Component({
  selector: 'app-dialog-pause',
  templateUrl: './pause.component.html',
  styleUrls: ['./pause.component.less']
})
export class PauseComponent implements DialogTemplate {
  @Input() config: DialogConfig | undefined;

  constructor(@Inject(PAUSE_TIMES) public times: string[]) { }


  get time(): string {
    return this.config?.data?.time || '';
  }
  set time(t: string) {
    if (this.config) {
      this.config.data = { time: t };
    }
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('config' in changes) {
      this.time = 'infinite';
    }
  }

  stripUnit(t: string): number {
    let idx = t.search('[^0-9]');
    if (idx >= 0) {
      return parseInt(t.substring(0, idx));
    }
    return parseInt(t);
  }
}
