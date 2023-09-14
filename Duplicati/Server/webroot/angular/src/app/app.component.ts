import { Component } from '@angular/core';
import { ServerSettingsService } from './services/server-settings.service';
import { ThemeService } from './services/theme.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.less']
})
export class AppComponent {
  title = 'angular';

  menuExpanded = false;

  constructor(private serverSettings: ServerSettingsService,
    private theme: ThemeService) { }

  ngOnInit() {
    this.serverSettings.initSettings();
    this.theme.loadCurrentTheme();
  }

  closeMenus(event: Event) {
    // TODO: dont close if clicked on the menu, but that is the same behavior as before
    this.menuExpanded = false;
  }
}
