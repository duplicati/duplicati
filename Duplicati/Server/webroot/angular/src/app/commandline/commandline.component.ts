import { Component, Input, SimpleChanges, ViewChild } from '@angular/core';
import { Router } from '@angular/router';
import { defaultIfEmpty, EMPTY, Subscription, timer } from 'rxjs';
import { BackupEditUriComponent } from '../edit-backup/backup-edit-uri/backup-edit-uri.component';
import { BackupService } from '../services/backup.service';
import { CommandlineService } from '../services/commandline.service';
import { DialogService } from '../services/dialog.service';
import { ParserService } from '../services/parser.service';
import { ServerStatus } from '../services/server-status';
import { ServerStatusService } from '../services/server-status.service';
import { CommandLineArgument, SystemInfo } from '../system-info/system-info';
import { SystemInfoService } from '../system-info/system-info.service';

@Component({
  selector: 'app-commandline',
  templateUrl: './commandline.component.html',
  styleUrls: ['./commandline.component.less']
})
export class CommandlineComponent {
  @Input() backupid?: string;
  @Input() viewid?: string;

  readonly pagesize = 100;

  mode: 'submit' | 'view' = 'submit';
  serverstate?: ServerStatus;
  private _extendedOptions: string[] = [];
  get extendedOptions(): string[] {
    return this._extendedOptions;
  }
  set extendedOptions(value: string[]) {
    this._extendedOptions = value;
    this.reloadOptionsList();
  }
  arguments: string[] = [];
  running: boolean = false;
  command?: string;
  editUriState: boolean = false;
  private _targetURL: string = '';
  get targetURL(): string {
    return this._targetURL;
  }
  set targetURL(value: string) {
    this._targetURL = value;
    this.reloadOptionsList();
  }
  viewLines: string[] = [];
  viewOffset: number = 0;
  started: boolean = false;
  finished: boolean = false;
  showAdvancedTextArea: boolean = false;
  rawFinished: boolean = false;
  viewRefreshing: boolean = false;

  supportedCommands: string[] = []
  commandHelp = new Map<string, string>();
  extendedOptionList: CommandLineArgument[] = [];

  @ViewChild(BackupEditUriComponent)
  private editUri?: BackupEditUriComponent;

  private systemInfo?: SystemInfo;

  private subscription?: Subscription;
  private timerSubscription?: Subscription;

  constructor(private commandlineService: CommandlineService,
    private dialog: DialogService,
    private parser: ParserService,
    private router: Router,
    private backupService: BackupService,
    private systemInfoService: SystemInfoService,
    public serverStatus: ServerStatusService) { }

  ngOnInit() {
    this.subscription = new Subscription();
    this.subscription.add(this.serverStatus.getStatus().subscribe(s => this.serverstate = s));
    this.subscription.add(this.systemInfoService.getState().subscribe(s => {
      this.systemInfo = s;
      this.reloadOptionsList();
    }));
    this.commandlineService.getSupportedCommands().subscribe(cmds => {
      this.supportedCommands = cmds;
      this.command = 'help';
    }, err => {
      this.dialog.connectionError('Failed to connect: ', err);
      this.router.navigate(['/']);
    });

    if (this.viewid != null) {
      this.mode = 'view';
      this.viewOffset = 0;
      this.fetchOutputLines();
    }

    if (this.backupid != null) {
      this.backupService.exportCommandLineArgs(this.backupid, true).subscribe(res => {
        this.targetURL = res.Backend;
        this.arguments = res.Arguments;
        this.extendedOptions = res.Options;
      }, this.dialog.connectionError('Failed to connect: '));
    }
  }

  ngOnDelete() {
    this.subscription?.unsubscribe();
  }

  submitURI() {
    (this.editUri?.buildUri() ?? EMPTY).pipe(defaultIfEmpty('')).subscribe(
      url => {
        this.targetURL = url;
        this.editUriState = false;
      }
    );
  }

  run() {
    if (this.command == null) {
      return;
    }
    let opts = this.parser.parseOptionStrings(this.extendedOptions);
    let combined = [...this.arguments];

    if (this.targetURL.length != 0) {
      combined.unshift(this.targetURL);
    }
    combined.unshift(this.command);

    for (let n in opts) {
      let value = opts[n];
      if (n == 'include' || n == 'exclude') {
        // Handle filters that appear multiple times
        for (let opt of value) {
          combined.push(`--${n}=${opt}`);
        }
      } else if (value == null) {
        combined.push(n);
      } else {
        combined.push(`${n}=${value}`);
      }
    }

    this.commandlineService.run(combined).subscribe(
      id => {
        this.router.navigate(['/commandline/view', id]);
      },
      this.dialog.connectionError('Failed to connect: '));
  }

  reloadOptionsList() {
    let opts = this.parser.parseOptionStrings(this.extendedOptions);
    if (!opts || !this.systemInfo) {
      return;
    }
    const encmodule = opts['encryption-module'] || opts['--encryption-module'] || '';
    const compmodule = opts['compression-module'] || opts['--compression-module'] || 'zip';
    let backmodule = this.targetURL || '';
    let ix = backmodule.indexOf(':');
    if (ix > 0) {
      backmodule = backmodule.substr(0, ix);
    }
    this.extendedOptionList = this.parser.buildOptionList(this.systemInfo, encmodule, compmodule, backmodule);
  }

  abort() {
    if (this.viewid) {
      this.commandlineService.abort(this.viewid).subscribe({ error: this.dialog.connectionError('Failed to connect: ') });
    }
  }

  fetchOutputLines() {
    if (this.viewRefreshing || this.viewid == null) {
      return;
    }

    this.viewRefreshing = true;
    if (this.timerSubscription != null) {
      this.timerSubscription?.unsubscribe();
      this.timerSubscription = undefined;
    }
    this.commandlineService.getOutput(this.viewid, this.pagesize, this.viewOffset).subscribe(
      res => {
        this.viewLines.push(...res.Items);
        this.viewOffset += res.Items.length;

        while (this.viewLines.length > 2000) {
          this.viewLines.shift();
        }
        this.started = res.Started;
        this.finished = res.Finished && this.viewOffset == res.Count;
        this.viewRefreshing = false;

        let waitTime = 2000;
        // Fetch more as we are not empty
        if (res.Items.length != 0) {
          waitTime = 100;
        }
        // All done, slowly keep the data alive
        else if (res.Finished) {
          waitTime = 10000;
        }

        this.timerSubscription = timer(waitTime).subscribe(() => this.fetchOutputLines());
      }, err => {
        if (err?.status == 404) {
          this.viewLines.push('Connection lost, data has expired ...');
          this.finished = true;
        } else {
          this.viewLines.push('Connection error, retry in 2 sec ...');
          this.viewRefreshing = false;
          this.timerSubscription = timer(2000).subscribe(() => this.fetchOutputLines());
        }
      }
    );
  }
}
