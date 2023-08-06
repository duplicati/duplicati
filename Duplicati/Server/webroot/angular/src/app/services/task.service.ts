import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class TaskService {

  constructor(private client: HttpClient) { }

  stopAfterCurrentFile(taskId: number): Observable<void> {
    return this.client.post<void>(`/task/${taskId}/stopaftercurrentfile`, '');
  }

  stopNow(taskId: number): Observable<void> {
    return this.client.post<void>(`/task/${taskId}/stopnow`, '');
  }

  abort(taskId: number): Observable<void> {
    return this.client.post<void>(`/task/${taskId}/abort`, '');
  }
}
