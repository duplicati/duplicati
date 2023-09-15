import { Component, Input } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-main-menu',
  templateUrl: './main-menu.component.html',
  styleUrls: ['./main-menu.component.less']
})
export class MainMenuComponent {

  public current_page: string = 'home';
  public get state(): ServerStatus {
    return this.serverStatus.status;
  }
  public isLoggedIn: boolean = true;

  @Input() openOnMobile: boolean = false;

  private subscription?: Subscription;

  constructor(private systemInfo: SystemInfoService, private serverStatus: ServerStatusService) { }


  public resume(): void {
    this.serverStatus.resume();
  }
  public log_out(): void {
  }
}
