import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-external-link',
  templateUrl: './external-link.component.html',
  styleUrls: ['./external-link.component.less']
})
export class ExternalLinkComponent {
  @Input() link?: string;
  @Input() title?: string;
}
