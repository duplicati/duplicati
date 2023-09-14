import { booleanAttribute } from '@angular/core';
import { Component, Input } from '@angular/core';
import { Subscription } from 'rxjs';
import { ProgressService } from '../services/progress.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { TaskService } from '../services/task.service';

@Component({
  selector: 'app-wait-area',
  templateUrl: './wait-area.component.html',
  styleUrls: ['./wait-area.component.less']
})
export class WaitAreaComponent {
  @Input({ required: true }) taskid!: number;
  @Input() text?: string;
  @Input({ transform: booleanAttribute }) allowCancel: boolean = false;

  serverstate?: ServerStatus;
  statusText?: string;

  private subscription?: Subscription;

  constructor(public serverStatus: ServerStatusService,
    public progress: ProgressService,
    private task: TaskService) { }

  ngOnInit() {
    this.serverStatus.getStatus().subscribe(s => {
      this.serverstate = s;
      this.statusText = this.progress.getStatusText(s.lastPgEvent);
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  cancelTask() {
    this.task.stopNow(this.taskid);
  }
}
