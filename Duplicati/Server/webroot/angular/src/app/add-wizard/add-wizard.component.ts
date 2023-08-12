import { Component } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-add-wizard',
  templateUrl: './add-wizard.component.html',
  styleUrls: ['./add-wizard.component.less']
})
export class AddWizardComponent {

  selection = {
    style: 'blank'
  }

  constructor(private router: Router) {}

  nextPage(): void {
    if (this.selection.style === 'blank') {
      this.router.navigate(['/add']);
    } else {
      this.router.navigate(['/import']);
    }
  }
}
