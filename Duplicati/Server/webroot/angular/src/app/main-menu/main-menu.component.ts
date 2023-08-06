import { Component, Input } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-main-menu',
  templateUrl: './main-menu.component.html',
  styleUrls: ['./main-menu.component.less']
})
export class MainMenuComponent {

  public current_page: string = 'home';
  public state: any;
  public isLoggedIn: boolean = true;

  @Input() openOnMobile: boolean = false;

  constructor(private systemInfo: SystemInfoService) { }

  public resume(): void { }
  public log_out(): void { }
}
