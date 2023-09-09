import { Component, Input } from '@angular/core';
import { ConvertService } from '../../services/convert.service';

@Component({
  selector: 'app-delete-phase',
  templateUrl: './delete-phase.component.html'
})
export class DeletePhaseComponent {
  @Input({ required: true }) results: any;

  setsExpanded: boolean = false;

  constructor(public convert: ConvertService) { }

}
