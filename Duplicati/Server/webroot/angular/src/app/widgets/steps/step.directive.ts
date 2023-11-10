import { booleanAttribute } from '@angular/core';
import { Input, TemplateRef } from '@angular/core';
import { Directive } from '@angular/core';

@Directive({
  selector: '[appStep]'
})
export class StepDirective {

  constructor(public templateRef: TemplateRef<unknown>) { }

  // Step number (starts at 0)
  @Input({ required: true }) appStep!: number;
  @Input({ required: true }) name!: string;
  @Input({ transform: booleanAttribute }) enabled: boolean = true;
}
