import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ExpandBoxComponent } from './expand-box/expand-box.component';
import { MatMenuModule } from '@angular/material/menu';
import { AppRoutingModule } from '../app-routing.module';
import { ExpandMenuDirective } from './expand-box/expand-menu.directive';
import { InputMultiplierComponent } from './input-multiplier/input-multiplier.component';
import { FormsModule } from '@angular/forms';
import { StepsComponent } from './steps/steps.component';
import { StepDirective } from './steps/step.directive';



@NgModule({
  declarations: [
    ExpandBoxComponent,
    ExpandMenuDirective,
    InputMultiplierComponent,
    StepsComponent,
    StepDirective
  ],
  imports: [
    AppRoutingModule,
    CommonModule,
    MatMenuModule,
    FormsModule,
  ],
  exports: [
    ExpandBoxComponent,
    ExpandMenuDirective,
    InputMultiplierComponent,
    StepsComponent,
    StepDirective
  ]
})
export class WidgetsModule { }
