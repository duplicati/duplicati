import { Component } from '@angular/core';
import { BackupListService } from '../backup-list.service';

@Component({
  selector: 'app-home',
  templateUrl: './home.component.html',
  styleUrls: ['./home.component.less']
})
export class HomeComponent {
  public backups: any[] = [];

  constructor(private backupListService: BackupListService) { }

  ngOnInit(): void {
    this.backupListService.getBackups().subscribe(b => this.backups = b);
  }
}
