import { Component } from '@angular/core';

@Component({
  selector: 'app-connection-lost',
  templateUrl: './connection-lost.component.html',
  styleUrls: ['./connection-lost.component.less']
})
export class ConnectionLostComponent {

  connectionState: string = 'connected';
  connectionTimer: string = '';

  doReconnect(): void {

  }
}
