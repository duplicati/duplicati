import { Component, SecurityContext } from '@angular/core';
import { Subscription } from 'rxjs';
import { NotificationService } from '../services/notification.service';
import { Notification } from '../services/notification';
import { ServerStatus } from '../services/server-status';
import { BackupService } from '../services/backup.service';
import { HttpErrorResponse } from '@angular/common/http';
import { DialogService } from '../services/dialog.service';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { UrlService } from '../services/url.service';
import { Router } from '@angular/router';
import { UpdateService } from '../services/update.service';

@Component({
  selector: 'app-notification-area',
  templateUrl: './notification-area.component.html',
  styleUrls: ['./notification-area.component.less']
})
export class NotificationAreaComponent {

  Notifications: Notification[] = [];
  DownloadLink?: SafeResourceUrl;
  status?: ServerStatus;

  private subscription?: Subscription;
  constructor(private notificationService: NotificationService,
    private backupService: BackupService,
    private updateService: UpdateService,
    private dialog: DialogService,
    private sanitizer: DomSanitizer,
    private router: Router,
    private urlService: UrlService) { }

  ngOnInit() {
    this.subscription = this.notificationService.getNotifications().subscribe(n => this.Notifications = n);
  }
  ngOnDestroy() {
    this.subscription?.unsubscribe();
  }

  trackByID(idx: number, item: any): any {
    return item.ID;
  }

  doDismissAll() {
    this.notificationService.dismissAll();
  }
  doDismiss(id: number) {
    this.notificationService.dismiss(id);
  }
  doShowLog(backupid: string) {
    this.backupService.isActive(backupid).subscribe({
      next: () => {
        this.router.navigate(['/log', backupid]);
      }, error: (resp: HttpErrorResponse) => {
        if (resp.status === 404) {
          if (parseInt(backupid) + '' !== backupid) {
            this.dialog.dialog('Error', 'The backup was temporary and does not exist anymore, so the log data is lost');
          } else {
            this.dialog.dialog('Error', 'The backup is missing, has it been deleted?');
          }
        } else {
          this.dialog.connectionError('Failed to find backup: ', resp);
        }
      }
    })
  }

  doRepair(backupid: string) {
    this.backupService.doRepair(backupid);
  }
  doShowUpdate() {
    this.router.navigate(['/updatechangelog']);
  }
  doInstallUpdate(id: number) {
    this.updateService.startUpdateDownload();
  }
  doActivateUpdate(id: number) {
    this.updateService.startUpdateActivate();
  }
  doDownloadBugreport(n: Notification) {
    const id = n.Action.substr('bug-report:created:'.length);
    const downloadLink = this.urlService.getBugreportUrl(id);
    this.DownloadLink = this.sanitizer.bypassSecurityTrustResourceUrl(downloadLink);
    n.DownloadLink = downloadLink;
  }
}
