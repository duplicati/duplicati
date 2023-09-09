import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class IconService {

  constructor() { }

  resultIcon(parsedResult: string | undefined): string {
    if (parsedResult == 'Success') {
      return 'fa fa-check-circle success-color';
    } else if (parsedResult == 'Warning') {
      return 'fa fa-exclamation-circle warning-color';
    } else if (parsedResult == 'Error') {
      return 'fa fa-times-circle error-color';
    } else {
      return 'fa fa-question-circle';
    }
  }
}
