import { Directive, ViewContainerRef } from '@angular/core';

@Directive({
  selector: '[appEditorHost]'
})
export class EditorHostDirective {

  constructor(public viewContainerRef: ViewContainerRef) { }

}
