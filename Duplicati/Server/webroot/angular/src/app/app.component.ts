import { Component } from '@angular/core';
import { ServerSettingsService } from './services/server-settings.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.less']
})
export class AppComponent {
  title = 'angular';

  menuExpanded = false;

  constructor(private serverSettings: ServerSettingsService) { }

  ngOnInit() {
    this.serverSettings.initSettings();
  }

  closeMenus(event: Event) {
    // TODO: dont close if clicked on the menu, but that is the same behavior as before
    this.menuExpanded = false;
  }
}
