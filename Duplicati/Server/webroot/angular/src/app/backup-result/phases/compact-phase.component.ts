import { Component, Input } from '@angular/core';
import { ConvertService } from '../../services/convert.service';

@Component({
  selector: 'app-compact-phase',
  templateUrl: './compact-phase.component.html'
})
export class CompactPhaseComponent {
  @Input({ required: true }) results: any;

  constructor(public convert: ConvertService) { }
}
