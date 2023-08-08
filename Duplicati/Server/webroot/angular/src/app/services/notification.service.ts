import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { catchError, distinctUntilChanged, map, Observable, ReplaySubject, skip, Subscription } from 'rxjs';
import { ServerStatusService } from './server-status.service';
import { Notification } from './notification';

@Injectable({
  providedIn: 'root'
})
export class NotificationService {

  constructor(private serverStatus: ServerStatusService,
    private client: HttpClient) { }

  private isRefreshing = false;
  private needsRefresh = false;

  private notifications: Notification[] = [];
  private notifications$?: ReplaySubject<Notification[]>;
  private statusSubscription?: Subscription;
  private requestSubscription?: Subscription;

  getNotifications(): Observable<Notification[]> {
    if (this.notifications$ === undefined) {
      this.notifications$ = new ReplaySubject<Notification[]>(1);
      new Subscription();
      // We always refresh, so no need to use the initial event
      const shouldSkipFirst = this.serverStatus.status.lastNotificationUpdateId === -1;
      this.statusSubscription = this.serverStatus.getStatus().pipe(
        map(s => s.lastNotificationUpdateId),
        skip(shouldSkipFirst ? 1 : 0),
        distinctUntilChanged())
        .subscribe(id => {
          this.refreshNotifications();
        });
      this.refreshNotifications();
    }
    return this.notifications$.asObservable();
  }

  dismiss(id: number): void {
    this.client.delete<void>('/notification/' + id).subscribe({
      error: () => {
        // Most likely there was a sync problem, so attempt to reload
        this.refreshNotifications();
      }
    });
  }

  dismissAll(): void {
    for (let n of this.notifications) {
      this.dismiss(n.ID);
    }
  }

  private refreshNotifications(): void {
    if (this.isRefreshing) {
      this.needsRefresh = true;
      return;
    }

    this.needsRefresh = false;
    this.isRefreshing = true;

    this.requestSubscription?.unsubscribe();

    this.requestSubscription = this.client.get<Notification[]>('/notifications').subscribe(data => {
      let idmap = new Map<number, Notification>();
      for (let entry of data) {
        idmap.set(entry.ID, entry);
      }

      // Sync map and list
      for (let i = this.notifications.length - 1; i >= 0; --i) {
        if (!idmap.has(this.notifications[i].ID)) {
          this.notifications.splice(i, 1);
        } else {
          idmap.delete(this.notifications[i].ID);
        }
      }

      // Then add all new items
      for (let item of idmap.values()) {
        this.notifications.push(item);
      }

      this.notifications.sort((a, b) => {
        return a.ID - b.ID;
      });

      this.notifications$?.next(this.notifications);

      this.isRefreshing = false;
      if (this.needsRefresh) {
        this.refreshNotifications();
      }
    });
  }
}
