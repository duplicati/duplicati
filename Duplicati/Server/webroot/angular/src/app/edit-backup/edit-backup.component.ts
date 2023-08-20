import { Component } from '@angular/core';
import { ActivatedRoute, IsActiveMatchOptions, Router } from '@angular/router';
import { Backup, Schedule } from '../backup';
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

  constructor(private router: Router, private route: ActivatedRoute) { }

  ngOnInit() {
    if (!this.route.snapshot.paramMap.has('step')) {
      this.router.navigate([{ step: 0 }]);
    }
    this.route.paramMap.subscribe(params => {
      this.CurrentStep = parseInt(params.get('step') || '0');
    });
  }

  nextStep() {
    if (this.CurrentStep < 5) {
      this.router.navigate([{ step: this.CurrentStep + 1 }]);
    }
  }

  prevStep() {
    if (this.CurrentStep > 0) {
      this.router.navigate([{ step: this.CurrentStep - 1 }]);
    }
  }
}
