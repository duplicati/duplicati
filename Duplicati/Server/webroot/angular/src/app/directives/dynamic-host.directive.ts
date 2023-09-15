import { Input, Type } from '@angular/core';
import { Directive, ViewContainerRef } from '@angular/core';

@Directive({
  selector: '[appDynamicHost]'
})
export class DynamicHostDirective {

  constructor(public viewContainerRef: ViewContainerRef) { }

}
