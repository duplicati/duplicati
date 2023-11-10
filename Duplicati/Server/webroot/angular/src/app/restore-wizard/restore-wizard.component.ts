import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AddOrUpdateBackupData } from '../backup';
import { BackupListService } from '../services/backup-list.service';
import { ConvertService } from '../services/convert.service';

@Component({
  selector: 'app-restore-wizard',
  templateUrl: './restore-wizard.component.html',
  styleUrls: ['./restore-wizard.component.less']
})
export class RestoreWizardComponent {
  selection?: 'direct' | 'import' | { backupId: string };

  backups: AddOrUpdateBackupData[] = [];

  constructor(public convert: ConvertService,
    private backupList: BackupListService,
    private router: Router) { }

  ngOnInit() {
    this.backupList.getBackups().subscribe(b => this.backups = b);
  }

  parseInt = parseInt;

  nextPage() {
    if (this.selection === 'direct') {
      this.router.navigate(['/restoredirect']);
    } else if (this.selection === 'import') {
      this.router.navigate(['/restore-import']);
    } else if (this.selection != null) {
      this.router.navigate(['/restore', this.selection.backupId]);
    }
  }

  getLastDuration(metadata: Record<string, string>): string | undefined {
    return this.convert.formatDuration(metadata['LastRestoreDuration']) || $localize`0 seconds`;
  }
}
