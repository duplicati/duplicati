import { Component, Input, SecurityContext, SimpleChange, SimpleChanges } from '@angular/core';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';

@Component({
  selector: 'app-external-link',
  templateUrl: './external-link.component.html',
  styleUrls: ['./external-link.component.less']
})
export class ExternalLinkComponent {
  @Input() link?: string;
  @Input() title?: string;
}
