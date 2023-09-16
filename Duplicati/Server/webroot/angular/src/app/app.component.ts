import { Inject } from '@angular/core';
import { Component } from '@angular/core';
import { loadTranslations } from '@angular/localize';
import { Title } from '@angular/platform-browser';
import { combineLatest, map } from 'rxjs';
import { Subscription } from 'rxjs';
import { distinctUntilChanged, startWith, take, withLatestFrom } from 'rxjs';
import { BrandingService } from './services/branding.service';
import { LocalizationService } from './services/localization.service';
import { ServerSettingsService } from './services/server-settings.service';
import { ThemeService } from './services/theme.service';
import { SystemInfoService } from './system-info/system-info.service';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.less']
})
export class AppComponent {

  menuExpanded = false;
  private titleSubscription?: Subscription;
  finishedLoading = false;

  constructor(private title: Title,
    private serverSettings: ServerSettingsService,
    private systemInfo: SystemInfoService,
    private brandingService: BrandingService,
    private localizationService: LocalizationService,
    private theme: ThemeService) { }

  ngOnInit() {
    this.serverSettings.initSettings();
    // We are using runtime translations using loadTranslations() to avoid building a bundle for each language
    // No component template can be displayed before the translations are loaded,
    // so set them to blank. This is not nice with slow connections, but it works
    this.localizationService.loadLanguage().subscribe({
      error: () => this.finishedLoading = true,
      complete: () => this.finishedLoading = true
    });
    this.theme.loadCurrentTheme();
    this.titleSubscription = combineLatest(this.systemInfo.getState().pipe(startWith(null)), this.brandingService.getAppName()).subscribe(
      v => {
        const machineName = v[0]?.MachineName || '';
        const appName = v[1];
        this.title.setTitle(`${machineName} - ${appName}`);
      }
    );
  }

  ngOnDestroy() {
    this.titleSubscription?.unsubscribe();
  }

  closeMenus(event: Event) {
    // TODO: dont close if clicked on the menu, but that is the same behavior as before
    this.menuExpanded = false;
  }
}
