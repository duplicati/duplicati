import { Component, Input } from '@angular/core';
import { ConvertService } from '../../services/convert.service';

@Component({
  selector: 'app-purge-phase',
  templateUrl: './purge-phase.component.html'
})
export class PurgePhaseComponent {
  @Input({ required: true }) results: any;

  constructor(public convert: ConvertService) { }
}
