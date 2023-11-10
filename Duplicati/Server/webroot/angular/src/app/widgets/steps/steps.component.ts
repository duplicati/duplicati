import { ContentChildren, Input, QueryList } from '@angular/core';
import { Component } from '@angular/core';
import { ActivatedRoute, IsActiveMatchOptions } from '@angular/router';
import { StepDirective } from './step.directive';

@Component({
  selector: 'app-steps',
  templateUrl: './steps.component.html',
  styleUrls: ['./steps.component.less']
})
export class StepsComponent {
  isActiveOptions: IsActiveMatchOptions = {
    matrixParams: 'subset',
    queryParams: 'exact',
    paths: 'exact',
    fragment: 'ignored'
  };

  @Input() allowCallback?: (nextStep: number) => boolean;

  @ContentChildren(StepDirective)
  stepQuery!: QueryList<StepDirective>;

  steps: StepDirective[] = [];
  currentStep: number = 0;

  constructor(private route: ActivatedRoute) { }

  ngOnInit() {
    this.route.paramMap.subscribe(params => {
      this.currentStep = parseInt(params.get('step') || '0');
    });
  }

  ngAfterContentInit() {
    // Changing the steps is not supported for now
    if (this.stepQuery) {
      this.steps = this.stepQuery.toArray().sort((a, b) => a.appStep - b.appStep);
    }
  }
}
