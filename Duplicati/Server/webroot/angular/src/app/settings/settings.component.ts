import { Component, Input } from '@angular/core';
import { CookieService } from 'ngx-cookie-service';
import { Subscription } from 'rxjs';
import { ConvertService } from '../services/convert.service';
import { DialogService } from '../services/dialog.service';
import { ParserService } from '../services/parser.service';
import { ServerSettingsService } from '../services/server-settings.service';
import { CommandLineArgument, SystemInfo } from '../system-info/system-info';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-settings',
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.less']
})
export class SettingsComponent {
  private rawdata?: Record<string, string>;
  requireRemotePassword: boolean = false;
  remotePassword: string = '';
  confirmPassword: string = '';
  allowRemoteAccess: boolean = false;
  startupDelayDurationValue: string = '';
  startupDelayDurationMultiplier: string = '';
  updateChannel: string = '';
  originalUpdateChannel: string = '';
  usageReporterLevel: string = '';
  disableTrayIconLogin: boolean = false;
  remoteHostnames: string = '';
  advancedOptions: string[] = [];
  servermodulesettings: Record<string, string> = {};

  uiLanguage: string = '';
  langBrowserDefault: string = 'Browser default';
  langDefault: string = 'Default';
  theme: string = '';
  channelname: string = '';
  levelname: string = '';
  serverModules: any[] = [];
  showAdvancedTextArea: boolean = false;

  private subscription?: Subscription;
  systemInfo?: SystemInfo;
  private advancedOptionList?: CommandLineArgument[];

  constructor(private serverSettings: ServerSettingsService,
    private systemInfoService: SystemInfoService,
    public convert: ConvertService,
    public parser: ParserService,
    private dialog: DialogService) { }

  ngOnInit() {
    this.subscription = this.systemInfoService.getState().subscribe(s => this.systemInfo = s);
    this.subscription.add(this.serverSettings.getServerSettings().subscribe(s => this.updateSettings(s)));
  }

  reloadOptionsList() {
    if (this.systemInfo) {
      this.advancedOptionList = this.parser.buildOptionList(this.systemInfo, false, false, false);
      let mods = [];
      if (this.systemInfo.ServerModules != null) {
        for (let m of this.systemInfo.ServerModules) {
          if (m.SupportedGlobalCommands != null && m.SupportedGlobalCommands.length > 0) {
            mods.push(m);
          }
        }
      }

      this.serverModules = mods;
      this.parser.extractServerModuleOptions(this.advancedOptions, this.serverModules, this.servermodulesettings, 'SupportedGlobalCommands');
    }
  }

  updateSettings(s: Record<string, string>): void {
    this.rawdata = s;
    this.requireRemotePassword = s['server-passphrase'] != null && s['server-passphrase'] != '';
    this.remotePassword = s['server-phassphrase'] || '';
    this.confirmPassword = '';
    this.allowRemoteAccess = s['server-listen-interface'] !== 'loopback';
    const startupDelay = s['startup-delay'] || '';
    this.startupDelayDurationValue = (startupDelay.length == 0 || startupDelay.substr(0, startupDelay.length - 1) === '')
      ? '0' : startupDelay.substr(0, startupDelay.length - 1);
    this.startupDelayDurationMultiplier = (startupDelay.length == 0 || startupDelay.substr(-1) === '') ? 's' : startupDelay.substr(-1);
    this.updateChannel = s['update-channel'] || '';
    this.originalUpdateChannel = s['update-channel'] || '';
    this.usageReporterLevel = s['usage-reporter-level'] || '';
    this.disableTrayIconLogin = this.parser.parseBoolString(s['disable-tray-icon-login']);
    this.remoteHostnames = s['allowed-hostnames'];
    this.advancedOptions = this.parser.serializeAdvancedOptionsToArray(s);
    this.servermodulesettings = {};

    this.parser.extractServerModuleOptions(this.advancedOptions, this.serverModules, this.servermodulesettings, 'SupportedGlobalCommands');
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  save() {
    // TODO: Should probably not trim passwords?
    if (this.requireRemotePassword && this.remotePassword.trim().length === 0) {
      this.dialog.notifyInputError('Cannot use empty password');
      return;
    }

    if (this.rawdata == null) {
      this.dialog.alert('The settings are not available, try reloading the page');
      return;
    }

    let patchdata: Record<string, string | boolean | null> = {
      'server-passphrase': this.requireRemotePassword ? this.remotePassword : '',
      'allowed-hostnames': this.remoteHostnames,
      'server-listen-interface': this.allowRemoteAccess ? 'any' : 'loopback',
      'startup-delay': this.startupDelayDurationValue + '' + this.startupDelayDurationMultiplier,
      'update-channel': this.updateChannel,
      'usage-reporter-level': this.usageReporterLevel,
      'disable-tray-icon-login': this.disableTrayIconLogin
    };

    if (this.requireRemotePassword && this.rawdata['server-passphrase'] !== this.remotePassword) {
      if (this.remotePassword !== this.confirmPassword) {
        this.dialog.notifyInputError('The passwords do not match');
        return;
      }
      //patchdata['server-passphrase-salt'] = CryptoJS.lib.WordArray.random(256 / 8).toString(CryptoJS.enc.Base64);
      //patchdata['server-passphrase'] = CryptoJS.SHA256(CryptoJS.enc.Hex.parse(CryptoJS.enc.Utf8.parse($scope.remotePassword) + CryptoJS.enc.Base64.parse(patchdata['server-passphrase-salt']))).toString(CryptoJS.enc.Base64);
    } else if (!this.requireRemotePassword) {
      patchdata['server-passphrase-salt'] = null;
      patchdata['server-passphrase'] = null;
    }

    this.parser.mergeAdvancedOptions(this.advancedOptions, patchdata, this.rawdata);
    for (var n in this.servermodulesettings) {
      patchdata['--' + n] = this.servermodulesettings[n];
    }

    this.serverSettings.updateTheme(this.theme);
    this.serverSettings.updateSettings(patchdata).subscribe(() => {
      this.serverSettings.setUILanguage(this.uiLanguage);

      if (this.updateChannel !== this.originalUpdateChannel) {
        this.serverSettings.checkForUpdates().subscribe();
      }

      location.reload();
    }, this.dialog.connectionError('Failed to save: '));
  }

  suppressDonationMessages() { }
  displayDonationMessages() { }
}
