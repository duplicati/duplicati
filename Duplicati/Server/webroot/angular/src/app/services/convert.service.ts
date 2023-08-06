import { Injectable } from '@angular/core';
import { CookieService } from 'ngx-cookie-service';

@Injectable({
  providedIn: 'root'
})
export class ConvertService {

  private dateTimeFormatOptions: Intl.DateTimeFormatOptions = {
    dateStyle: 'medium',
    timeStyle: 'short'
  };
  private dateFormatOptions: Intl.DateTimeFormatOptions = {
    dateStyle: 'medium'
  };

  // TODO: Update from system settings --force-actual-date
  private forceActualDate: boolean = false;

  constructor(private cookies: CookieService) { }

  parseDate(v: string): Date {
    const msec = Date.parse(v);
    if (isNaN(msec)) {
      if (v.length == 16 && v[8] == 'T' && v[15] == 'Z') {
        v = v.substr(0, 4) + '-' + v.substr(4, 2) + '-' + v.substr(6, 2) + 'T' +
          v.substr(9, 2) + ':' + v.substr(11, 2) + ':' + v.substr(13, 2) + 'Z';
      }
      return new Date(v);
    } else {
      return new Date(msec);
    }
  }

  private getLocale(): string {
    const browser_lang = navigator.languages ? navigator.languages[0] : navigator.language;
    if (this.cookies.check('ui-locale')) {
      return this.cookies.get('ui-locale');
    }
    return browser_lang;
  }

  parseTimestamp(value: string | Date | number | undefined): Date | undefined {
    if (typeof value === 'string') {
      value = this.parseDate(value);
    } else if (typeof value === 'number') {
      value = new Date(value * 1000);
    }
    return value;
  }

  toDisplayDateAndTime(dt: Date): string {
    return dt.toLocaleString(this.getLocale(), this.dateTimeFormatOptions);
  }

  formatDate(dt: Date): string {
    if (this.forceActualDate) {
      return this.toDisplayDateAndTime(dt);
    } else {
      return dt.toLocaleDateString(this.getLocale(), this.dateFormatOptions);
    }
  }

  formatDuration(duration: string | undefined): string | undefined {
    // duration is a timespan string in format (dd.)hh:mm:ss.sss
    // the part (dd.) is as indicated optional
    if (duration === undefined) {
      return undefined;
    }

    const timespanArray = duration.split(':');
    if (timespanArray.length < 3) {
      return undefined;
    }

    if (timespanArray[0].indexOf('.') > 0) {
      timespanArray[0] = timespanArray[0].replace('.', " day(s) and ");
    }
    // round second according to ms
    timespanArray[2] = Math.round(parseFloat(timespanArray[2])).toString();
    // zero-padding
    if (timespanArray[2].length == 1) timespanArray[2] = '0' + timespanArray[2];

    return timespanArray.join(':');
  }
}
