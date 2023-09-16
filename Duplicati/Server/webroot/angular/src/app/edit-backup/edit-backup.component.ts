import { Component, ViewChild } from '@angular/core';
import { ActivatedRoute, IsActiveMatchOptions, Router } from '@angular/router';
import { Observable, Subscription } from 'rxjs';
import { AddOrUpdateBackupData, Backup, Schedule } from '../backup';
import { BackupDefaultsService } from '../services/backup-defaults.service';
import { BackupListService } from '../services/backup-list.service';
import { BackupService } from '../services/backup.service';
import { DialogService } from '../services/dialog.service';
import { EditBackupService, RejectToStep } from '../services/edit-backup.service';
import { ImportService } from '../services/import.service';
import { ParserService } from '../services/parser.service';
import { SystemInfoService } from '../system-info/system-info.service';
import { BackupDestinationSettingsComponent } from './backup-destination-settings/backup-destination-settings.component';
import { BackupOptions } from './backup-options';

@Component({
  selector: 'app-edit-backup',
  templateUrl: './edit-backup.component.html',
  styleUrls: ['./edit-backup.component.less']
})
export class EditBackupComponent {
  isActiveOptions: IsActiveMatchOptions = {
    matrixParams: 'subset',
    queryParams: 'exact',
    paths: 'exact',
    fragment: 'ignored'
  };

  CurrentStep: number = 0;
  schedule: Schedule | null = null;
  backup: Backup = {
    DBPath: '',
    Description: '',
    Filters: [],
    ID: '',
    IsTemporary: false,
    Metadata: {},
    Name: '',
    Settings: [],
    Sources: [],
    Tags: [],
    TargetURL: ''
  };
  options: BackupOptions = new BackupOptions();

  backupId?: string;

  private subscription?: Subscription;

  initialUri?: string;
  @ViewChild(BackupDestinationSettingsComponent)
  destinationSettings!: BackupDestinationSettingsComponent;

  constructor(private router: Router,
    private route: ActivatedRoute,
    private backupList: BackupListService,
    private backupService: BackupService,
    private backupDefaults: BackupDefaultsService,
    private dialog: DialogService,
    private importService: ImportService,
    private editBackupService: EditBackupService) {
  }

  ngOnInit() {
    // Replace current history state, so that navigating back skips past the last one
    this.router.navigate([{ step: 0 }], { relativeTo: this.route, replaceUrl: true });
    this.route.paramMap.subscribe(params => {
      this.CurrentStep = parseInt(params.get('step') || '0');
      let backupId = params.get('backupId') ?? undefined;
    });
    this.route.data.subscribe(data => {
      this.initialize(data['backup']);
    });
  }

  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  nextStep() {
    if (this.CurrentStep < 5) {
      this.router.navigate([{ step: this.CurrentStep + 1 }], { relativeTo: this.route });
    }
  }

  prevStep() {
    if (this.CurrentStep > 0) {
      this.router.navigate([{ step: this.CurrentStep - 1 }], { relativeTo: this.route });
    }
  }

  initialize(b: AddOrUpdateBackupData) {
    this.backup = b.Backup;
    this.schedule = b.Schedule;


    // If Description is anything other than a string, we are either creating a new
    // backup or something went wrong when retrieving an existing one
    // Either way we should set it to an empty string
    if (typeof this.backup.Description !== 'string') {
      this.backup.Description = '';
    }

    this.backup.Sources = this.backup.Sources ?? [];
    this.backup.Filters = this.backup.Filters ?? [];
    this.options = this.editBackupService.parseOptions(this.backup);
    this.initialUri = this.backup.TargetURL;

    if (this.schedule != null) {
      this.schedule.Time = this.editBackupService.initialScheduleTime(this.schedule.Time);
    }
  }

  save() {
    const uri = this.destinationSettings.getUri();
    if (uri == null) {
      this.router.navigate([{ step: 1 }], { relativeTo: this.route });
      return;
    }
    this.backup.TargetURL = uri;
    this.options.isNew = this.backupId == null;
    const result = this.editBackupService.makeBackupData(this.backup, this.schedule, this.options);
    this.editBackupService.checkBackup(result, this.options).then(() => {
      if (this.editBackupService.postValidate(result.Backup, result.Schedule, this.options)) {
        if (this.options.isNew) {
          this.backupList.addBackup(result).subscribe(() => this.router.navigate(['/']));
        } else {
          this.backupService.putBackup(this.backupId!, result).subscribe(() => this.router.navigate(['/']));
        }
      }
    }, reason => {
      if (reason instanceof RejectToStep) {
        this.router.navigate([{ step: reason.step }], { relativeTo: this.route });
      }
    });
  }
}
