import { formatDate } from '@angular/common';
import { Component, Input } from '@angular/core';
import { ConvertService } from '../../services/convert.service';

@Component({
  selector: 'app-test-phase',
  templateUrl: './test-phase.component.html'
})
export class TestPhaseComponent {
  @Input({ required: true }) results: any;

  constructor(public convert: ConvertService) { }
}
