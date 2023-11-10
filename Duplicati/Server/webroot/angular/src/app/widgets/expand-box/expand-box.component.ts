import { Component, ContentChild, Input } from '@angular/core';
import { ExpandMenuDirective } from './expand-menu.directive';

@Component({
  selector: 'app-expand-box',
  templateUrl: './expand-box.component.html',
  styleUrls: ['./expand-box.component.less']
})
export class ExpandBoxComponent {
  @Input() expanded: boolean = false;
  @ContentChild(ExpandMenuDirective) menu?: ExpandMenuDirective;

}
