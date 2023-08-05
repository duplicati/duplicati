import { Component } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-main-menu',
  templateUrl: './main-menu.component.html',
  styleUrls: ['./main-menu.component.less']
})
export class MainMenuComponent {

  public current_page: string = 'home';
  public state: any;
  public isLoggedIn: boolean = true;

  constructor() { }

  public resume(): void { }
  public log_out(): void { }
}
