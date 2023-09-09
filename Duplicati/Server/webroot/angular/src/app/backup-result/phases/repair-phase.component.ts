import { Component, Input } from '@angular/core';
import { ConvertService } from '../../services/convert.service';

@Component({
  selector: 'app-repair-phase',
  templateUrl: './repair-phase.component.html'
})
export class RepairPhaseComponent {
  @Input({ required: true }) results: any;

  constructor(public convert: ConvertService) { }
}
