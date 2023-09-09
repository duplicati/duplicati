import { Input } from '@angular/core';
import { Component } from '@angular/core';
import { ConvertService } from '../services/convert.service';
import { IconService } from '../services/icon.service';

@Component({
  selector: 'app-message-list',
  templateUrl: './message-list.component.html',
  styleUrls: ['./message-list.component.less']
})
export class MessageListComponent {
  @Input() messages?: string[];
  @Input() length?: number;
  @Input() type?: string;
  @Input() title?: string;

  get hasItems(): boolean {
    return this.length != null && this.length > 0;
  }

  constructor(public icon: IconService) { }

  expanded: boolean = false;
}
