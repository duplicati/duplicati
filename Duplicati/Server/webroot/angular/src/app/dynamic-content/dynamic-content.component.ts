import { ComponentRef, EventEmitter, Output, SimpleChanges } from '@angular/core';
import { Component, Input, Type, ViewChild } from '@angular/core';
import { DynamicHostDirective } from '../directives/dynamic-host.directive';

@Component({
  selector: 'app-dynamic-content',
  templateUrl: './dynamic-content.component.html',
  styleUrls: ['./dynamic-content.component.less']
})
export class DynamicContentComponent {
  @Input({ required: true }) type?: Type<unknown>;
  @Output() instantiated = new EventEmitter<ComponentRef<unknown> | undefined>();

  private activeType?: Type<unknown>;

  @ViewChild(DynamicHostDirective, { static: true })
  dynamicHost!: DynamicHostDirective;

  ngOnInit() {
    this.instantiateComponent();
  }

  ngOnChanges(changes: SimpleChanges) {
    if ('type' in changes && !changes['type'].isFirstChange()) {
      this.instantiateComponent();
    }
  }

  private instantiateComponent() {
    const viewContainerRef = this.dynamicHost.viewContainerRef;
    if (this.type == null || this.activeType != this.type) {
      viewContainerRef.clear();

      if (this.type != null) {
        const componentRef = viewContainerRef.createComponent(this.type);
        this.instantiated.emit(componentRef);
      }
    } else {
      this.instantiated.emit(undefined);
    }
  }
}
