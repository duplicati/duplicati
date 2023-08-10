import { HttpClient } from '@angular/common/http';
import { Component, Input, SimpleChanges } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { BrandingService } from '../services/branding.service';
import { DialogService } from '../services/dialog.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { UpdateService } from '../services/update.service';
import { ModuleDescription } from '../system-info/system-info';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-about',
  templateUrl: './about.component.html',
  styleUrls: ['./about.component.less']
})
export class AboutComponent {
  page: string = 'general';
  appName: string = '';
  dev1 = 'Kenneth Skovhede';
  dev2 = 'Rene Stach';
  mail1 = 'mailto:kenneth@duplicati.com';
  mail2 = 'mailto:rene@duplicati.com';
  websitename = 'duplicati.com';
  websitelink = 'https://duplicati.com';
  licensename = 'GNU Lesser General Public License';
  licenselink = 'https://www.gnu.org/licenses/lgpl.html';
  Acknowledgements?: string;

  version = 'unknown';
  status?: ServerStatus;

  private subscription?: Subscription;
  ChangeLog?: string;
  Licenses?: any[];

  licenses: Record<string, string> = {
    'MIT': 'http://www.linfo.org/mitlicense.html',
    'Apache': 'https://www.apache.org/licenses/LICENSE-2.0.html',
    'Apache 2': 'https://www.apache.org/licenses/LICENSE-2.0.html',
    'Apache 2.0': 'https://www.apache.org/licenses/LICENSE-2.0.html',
    'Public Domain': 'https://creativecommons.org/licenses/publicdomain/',
    'GPL': 'https://www.gnu.org/copyleft/gpl.html',
    'LGPL': 'https://www.gnu.org/copyleft/lgpl.html',
    'MS-PL': 'http://opensource.org/licenses/MS-PL',
    'Microsoft Public': 'http://opensource.org/licenses/MS-PL',
    'New BSD': 'http://opensource.org/licenses/BSD-3-Clause'
  };


  backendModules?: ModuleDescription[];
  encryptionModules?: ModuleDescription[];
  compressionModules?: ModuleDescription[];
  systemInfoProperties?: Map<string, string>;
  statusProperties?: Map<string, string>;

  constructor(private brandingService: BrandingService,
    private systemInfoService: SystemInfoService,
    private statusService: ServerStatusService,
    private updateService: UpdateService,
    private client: HttpClient,
    private dialog: DialogService,
    private router: Router,
    private route: ActivatedRoute) { }

  ngOnInit() {
    this.subscription = new Subscription();
    this.subscription.add(this.route.paramMap.subscribe(params => {
      this.setPage(params.get('page') || 'general');
    }));
    this.subscription.add(this.brandingService.getAppName().subscribe(n => this.appName = n));
    this.subscription.add(this.systemInfoService.getState().subscribe(info => {
      this.version = info.ServerVersionName;
      this.backendModules = info.BackendModules;
      this.encryptionModules = info.EncryptionModules;
      this.compressionModules = info.CompressionModules;
      this.systemInfoProperties = new Map<string, string>();
      for (let e of Object.entries(info)) {
        if (e[0] != 'Options' && e[0] != 'CompressionModules' && e[0] != 'EncryptionModules'
          && e[0] != 'BackendModules' && e[0] != 'GenericModules' && e[0] != 'WebModules'
          && e[0] != 'ConnectionModules' && e[0] != 'GroupedBackendModules') {
          let value = e[1];
          if (typeof value !== 'string') {
            value = JSON.stringify(value);
          }
          this.systemInfoProperties.set(e[0], value);
        }
      }
    }));
    this.subscription.add(this.statusService.getStatus().subscribe(status => {
      this.status = status;
      this.statusProperties = new Map<string, string>(Object.entries(status));
    }));
    this.client.get<any>('/acknowledgements').subscribe(resp => {
      this.Acknowledgements = resp.Acknowledgements as string;
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  setPage(p: string) {
    this.page = p;
    if (this.page === 'changelog' && this.ChangeLog == null) {
      this.client.get<any>('/changelog?from-update=false').subscribe(resp => {
        this.ChangeLog = resp.Changelog as string;
      });
    } else if (this.page === 'licenses' && this.Licenses == null) {
      this.client.get<any>('/licenses').subscribe(resp => {
        let res: any[] = [];
        for (let d of resp) {
          let r = JSON.parse(d.Jsondata);
          if (r != null) {
            r.licenselink = r.licenselink || this.licenses[r.license] || '#';
            res.push(r);
          }
        }
        this.Licenses = res;
      });
    }
  }
  doShowUpdateChangelog() {
    this.router.navigate(['updatechangelog']);
  }
  doStartUpdateDownload() {
    this.updateService.startUpdateDownload().subscribe();
  }
  doStartUpdateActivate() {
    this.updateService.startUpdateActivate().subscribe({
      error: this.dialog.connectionError('Activate failed: ')
    });
  }
  doCheckForUpdates() {
    this.updateService.checkForUpdates().subscribe();
  }

}
