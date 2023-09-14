import { formatDate } from '@angular/common';
import { Injectable } from '@angular/core';
import { CookieService } from 'ngx-cookie-service';
import { ServerSettingsService } from './server-settings.service';

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
  private timeFormatOptions: Intl.DateTimeFormatOptions = {
    timeStyle: 'short'
  }

  constructor(private cookies: CookieService,
    private serverSettings: ServerSettingsService) { }

  parseDate(v: string | number): Date {
    if (typeof v === 'string') {
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
    } else {
      return new Date(v);
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
    if (this.serverSettings.forceActualDate) {
      return this.toDisplayDateAndTime(dt);
    } else {
      const now = new Date();
      // TODO: Use some library to format like moment.js calendar()
      if (dt.getFullYear() == now.getFullYear()
        && dt.getMonth() == now.getMonth()
        && dt.getDate() == now.getDate()) {
        return 'Today at ' + dt.toLocaleTimeString(this.getLocale(), this.timeFormatOptions);
      } else {
        return dt.toLocaleDateString(this.getLocale(), this.dateFormatOptions);
      }
    }
  }

  formatTimestampToSeconds(timestamp: number | string) {
    return formatDate(timestamp, 'YYYY-MM-dd HH:mm:ss', 'en');
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

  formatSizeString(val: number | undefined) {
    if (val == null) {
      return '0 bytes';
    }
    var formatSizes = ['TB', 'GB', 'MB', 'KB'];
    //val = parseInt(val || 0);
    var max = formatSizes.length;
    for (var i = 0; i < formatSizes.length; i++) {
      var m = Math.pow(1024, max - i);
      if (val > m) {
        return (val / m).toFixed(2) + ' ' + formatSizes[i];
      }
    }

    return val + ' bytes';
  }

  pregQuote(str: string): string {
    // http://kevin.vanzonneveld.net
    // +   original by: booeyOH
    // +   improved by: Ates Goral (http://magnetiq.com)
    // +   improved by: Kevin van Zonneveld (http://kevin.vanzonneveld.net)
    // +   bugfixed by: Onno Marsman
    // *     example 1: pregQuote("$40");
    // *     returns 1: '\$40'
    // *     example 2: pregQuote("*RRRING* Hello?");
    // *     returns 2: '\*RRRING\* Hello\?'
    // *     example 3: pregQuote("\\.+*?[^]$(){}=!<>|:");
    // *     returns 3: '\\\.\+\*\?\[\^\]\$\(\)\{\}\=\!\<\>\|\:'

    return (str + '').replace(/([\\\.\+\*\?\[\^\]\$\(\)\{\}\=\!\<\>\|\:])/g, "\\$1");
  }

  replaceAllInsensitive(str: string, pattern: string, replacement: string) {
    return str.replace(new RegExp(`(${this.pregQuote(pattern)})`, 'gi'), replacement);
  }

  replaceAll(str: string, pattern: string, replacement: string) {
    return str.replace(new RegExp(`(${this.pregQuote(pattern)})`, 'g'), replacement);
  }

  removeEmptyEntries(l: (string | undefined | null)[]): string[] {
    // null or undefined values are removed
    return l.filter(v => v != null && v.trim().length > 0) as string[];
  }

  format(...args: string[]): string | null {
    if (args == null || args.length < 1) {
      return null;
    }
    let msg = args[0];
    if (args.length == 1) {
      return msg;
    }

    for (let i = 0; i < args.length; ++i) {
      msg = this.replaceAll(msg, `{${i - 1}}`, args[i]);
    }
    return msg;
  }

  globToRegexp(str: string): string {
    // Escape special chars, except ? and *
    str = (str + '').replace(/([\\\.\+\[\^\]\$\(\)\{\}\=\!\<\>\|\:])/g, "\\$1");
    // Replace ? and * with .? and .*
    return str.replace(/(\?|\*)/g, ".$1");
  }
}
