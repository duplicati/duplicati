import { Injectable } from '@angular/core';
import { CommandLineArgument, ModuleDescription, SystemInfo } from '../system-info/system-info';
import { ConvertService } from './convert.service';

@Injectable({
  providedIn: 'root'
})
export class ParserService {

  exampleOptionString = 'Enter one option per line in command-line format, eg. {0}';

  get speedMultipliers(): ({ name: string, value: string })[] {
    return [
      { name: 'bytes/s', value: 'b' },
      { name: 'KByte/s', value: 'KB' },
      { name: 'MByte/s', value: 'MB' },
      { name: 'GByte/s', value: 'GB' },
      { name: 'TByte/s', value: 'TB' }
    ];
  }
  get fileSizeMultipliers(): ({ name: string, value: string })[] {
    return [
      { name: 'byte', value: 'b' },
      { name: 'KByte', value: 'KB' },
      { name: 'MByte', value: 'MB' },
      { name: 'GByte', value: 'GB' },
      { name: 'TByte', value: 'TB' }
    ];
  }
  get timerangeMultipliers(): ({ name: string, value: string })[] {
    return [
      { name: 'Minutes', value: 'm' },
      { name: 'Hours', value: 'h' },
      { name: 'Days', value: 'D' },
      { name: 'Weeks', value: 'W' },
      { name: 'Months', value: 'M' },
      { name: 'Years', value: 'Y' }
    ];
  }
  get shorttimerangeMultipliers(): ({ name: string, value: string })[] {
    return [
      { name: 'Seconds', value: 's' },
      { name: 'Minutes', value: 'm' },
      { name: 'Hours', value: 'h' }
    ];
  }
  get daysOfWeek(): ({ name: string, value: string })[] {
    return [
      { name: 'Mon', value: 'mon' },
      { name: 'Tue', value: 'tue' },
      { name: 'Wed', value: 'wed' },
      { name: 'Thu', value: 'thu' },
      { name: 'Fri', value: 'fri' },
      { name: 'Sat', value: 'sat' },
      { name: 'Sun', value: 'sun' },
    ];
  }

  constructor() { }

  parseBoolString(text: string | undefined, def?: boolean): boolean {
    text = (text || '').toLowerCase();
    if (text === '0' || text === 'false' || text === 'off' || text === 'no' || text === 'f') {
      return false;
    } else if (text === '1' || text === 'true' || text === 'on' || text === 'yes' || text === 't') {
      return true;
    }
    return def === undefined ? false : def;
  }

  splitSizeString(text: string): [number, string | null] {
    const m = (/(\d*)(\w*)/mg).exec(text);
    if (!m) {
      return [parseInt(text), null];
    } else {
      return [parseInt(m[1]), m[2]];
    }
  }

  parseSizeString(val: string) {
    if (val == null) {
      return null;
    }
    var split = this.splitSizeString(val.toUpperCase());
    var formatSizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    var idx = formatSizes.indexOf((split[1] || '').toUpperCase());
    if (idx == -1) {
      idx = 0;
    }
    return split[0] * Math.pow(1024, idx);
  }

  serializeAdvancedOptionsToArray(options: Record<string, string> | Map<string, string>): string[] {
    let res: string[] = [];
    if (options instanceof Map) {
      for (let [k, v] of options.entries()) {
        if (k.indexOf('--') == 0) {
          res.push(k + '=' + v);
        }
      }
    } else {
      for (let k in options) {
        if (k.indexOf('--') == 0) {
          res.push(k + '=' + options[k]);
        }
      }
    }


    return res;
  }

  extractServerModuleOptions(advancedOptions: string[], servermodulelist: any[], servermodulesettings: Record<string, string>, optionlistname?: string) {
    if (optionlistname == null) {
      optionlistname = 'SupportedCommands';
    }

    for (let module of servermodulelist) {
      for (let option of module[optionlistname]) {
        const prefixstr = `--${option.Name}=`;
        for (let i = advancedOptions.length - 1; i >= 0; i--) {
          if (advancedOptions[i].indexOf(prefixstr) == 0) {
            servermodulesettings[option.Name] = advancedOptions[i].substring(prefixstr.length);
            advancedOptions.splice(i, 1);
          }
        }
      }
    }
  }

  buildOptionList(sysInfo: SystemInfo, encmodule: boolean | string, compmodule: boolean | string, backmodule: boolean | string): CommandLineArgument[] {
    if (sysInfo == null || sysInfo.Options == null) {
      return [];
    }

    let items = structuredClone(sysInfo.Options);
    for (let item of items) {
      item.Category = 'Core options';
    }

    let copyToList = (lst: ModuleDescription[], key?: string | boolean) => {
      if (typeof key != 'string') {
        key = undefined;
      }

      for (let e of lst) {
        if (key == null || key.toLowerCase() == e.Key.toLowerCase()) {
          let m = structuredClone(e.Options);
          if (m != null) {
            for (let item of m) {
              item.Category = e.DisplayName;
            }
            items.push.apply(items, m);
          }
        }
      }
    };

    copyToList(sysInfo.GenericModules);

    if (encmodule !== false) {
      copyToList(sysInfo.EncryptionModules, encmodule);
    }
    if (compmodule !== false) {
      copyToList(sysInfo.CompressionModules, compmodule);
    }
    if (backmodule !== false) {
      copyToList(sysInfo.BackendModules, backmodule);
    }

    return items;
  }

  parseOptionStrings(val: string | string[], dict?: Record<string, any>, validateCallback?: (dict: Record<string, any>, key: string, value: string | boolean) => boolean): Record<string, any> | null {
    dict = dict || {};
    let lines: string[];
    if (val != null && typeof val === typeof ([])) {
      lines = val as string[];
    } else {
      lines = (val as string || '').replaceAll(/\r/g, '\n').split('\n');
    }

    for (let line of lines) {
      line = line.trim();
      if (line !== '' && line[0] != '#') {
        if (line.indexOf('--') == 0) {
          line = line.substr(2);
        }

        let eqpos = line.indexOf('=');
        let key = line;
        let value: boolean | string = true;
        if (eqpos > 0) {
          key = line.substr(0, eqpos).trim();
          value = line.substr(eqpos + 1).trim();
          if (value == '')
            value = true;
        }

        if (validateCallback)
          if (!validateCallback(dict, key, value))
            return null;

        dict['--' + key] = value;
      }
    }
    return dict;
  }

  parseExtraOptions(str: string[], dict: Record<string, any>): boolean | string {
    let duplicate: string | undefined = undefined;
    let res = this.parseOptionStrings(str, dict, (d, k, v) => {
      if (d['--' + k] !== undefined) {
        duplicate = k;
        return false;
      }
      return true;
    }) != null;
    if (res) {
      return true;
    }
    return duplicate!;
  }

  mergeAdvancedOptions(advStr: string[], target: Record<string, any>, source: Record<string, string>): boolean {
    var adv = {}
    if (!this.parseExtraOptions(advStr, adv)) {
      return false;
    }

    Object.assign(target, adv);

    // Remove advanced options, no longer in the list
    for (var n in source)
      if (n.indexOf('--') == 0)
        if (target[n] === undefined)
          target[n] = null;

    return true;
  }

  parseOptionString(option: string): { name: string, value: string | null } {
    let idx = option.indexOf('=');
    if (idx >= 0) {
      return {
        name: option.substr(0, idx),
        value: option.substr(idx + 1)
      };
    } else {
      return { name: option, value: null };
    }
  }

  parseOptionFlags(option: string, flags?: string[]): { name: string, value: string[] } {
    let idx = option.indexOf('=');
    if (idx >= 0) {
      let name = option.substr(0, idx);
      let value = option.substr(idx + 1);

      let vals: string[] = [];

      if (value.indexOf(',') >= 0) {
        vals = value.split(',');
      } else {
        vals.push(value);
      }
      if (flags !== undefined) {
        // Match case of given values
        let result: string[] = [];
        for (let v of vals) {
          let f = flags.find(f => f.toLowerCase() === v.toLowerCase());
          if (f !== undefined) {
            result.push(f);
          } else {
            result.push(v);
          }
        }
        return { name: name, value: result };
      } else {
        return { name: name, value: vals };
      }
    }
    return { name: option, value: [] };
  }

  parseOptionEnum(option: string, values?: string[]): { name: string, value: string | null } {
    let res = this.parseOptionString(option);
    if (res.value != null && values != null) {
      // Match case of given values
      let v = values.find(v => v.toLowerCase() === res.value?.toLowerCase());
      if (v !== undefined) {
        res.value = v;
      }
    }
    return res;
  }

  parseOptionBool(option: string): { name: string, value: boolean | null } {
    let res = this.parseOptionString(option);
    if (res.value != null) {
      return { name: res.name, value: this.parseBoolString(res.value, true) };
    }
    return { name: res.name, value: null };
  }

  parseOptionInteger(option: string): { name: string, value: number | null } {
    let res = this.parseOptionString(option);
    if (res.value != null) {
      return { name: res.name, value: parseInt(res.value, 10) };
    }
    return { name: res.name, value: null };
  }

  parseOptionSize(option: string, sizeCase?: 'lowercase' | 'uppercase'): { name: string, value: number | null, multiplier: string | null } {
    let res = this.parseOptionString(option);
    if (res.value != null) {
      let parts = this.splitSizeString(res.value);
      if (parts != null) {
        let multiplier: string | null;
        if (parts[1] != null && sizeCase === 'lowercase') {
          multiplier = parts[1].toLowerCase();
        } else if (parts[1] != null && sizeCase === 'uppercase') {
          multiplier = parts[1].toUpperCase();
        } else {
          multiplier = parts[1];
        }
        return { name: res.name, value: parts[0], multiplier: multiplier };
      }
    }
    return { name: res.name, value: null, multiplier: null };
  }


  private URL_REGEXP_FIELDS = ['source_uri', 'backend-type', '--auth-username', '--auth-password', 'server-name', 'server-port', 'server-path', 'querystring'];
  private URL_REGEXP = /([^:]+)\:\/\/(?:(?:([^\:]+)(?:\:?:([^@]*))?\@))?(?:([^\/\?\:]*)(?:\:(\d+))?)(?:\/([^\?]*))?(?:\?(.+))?/;
  // Same as URL_REGEXP, but also accepts :\\ as a separator between drive letter (server for legacy reasons) and path
  private FILE_REGEXP = /(file)\:\/\/(?:(?:([^\:]+)(?:\:?:([^@]*))?\@))?(?:([^\/\?\:]*)(?:\:(\d+))?)(?:(?:\/|\:\\)([^\?]*))?(?:\?(.+))?/;
  private QUERY_REGEXP = /(?:^|&)([^&=]*)=?([^&]*)/g;

  decodeBackendUri(uri: string, backendSchemes?: Map<string, string>): Map<string, string> {
    let res = new Map<string, string>();


    // File URLs contain backslashes on Win which breaks the other regexp
    // This is not standard, but for compatibility it is allowed for now
    const fileMatch = this.FILE_REGEXP.exec(uri);
    if (fileMatch) {
      for (let i = 0; i < this.URL_REGEXP_FIELDS.length; ++i) {
        res.set(this.URL_REGEXP_FIELDS[i], fileMatch[i] || '');
      }
    } else {
      const m = this.URL_REGEXP.exec(uri);

      // Invalid URI
      if (!m) {
        return res;
      }

      for (let i = 0; i < this.URL_REGEXP_FIELDS.length; ++i) {
        res.set(this.URL_REGEXP_FIELDS[i], m[i] || '');
      }
    }

    let querystring = res.get('querystring');
    if (querystring !== undefined) {
      querystring = querystring.replace(this.QUERY_REGEXP, (str, key, val) => {
        if (key) {
          res.set('--' + key, decodeURIComponent((val || '').replace(/\+/g, '%20')));
        }
        return '';
      });
      res.set('querystring', querystring);
    }
    // Replace secure variants
    let scheme = res.get('backend-type');
    if (scheme && scheme.endsWith('s') && !backendSchemes?.has(scheme) && backendSchemes?.has(scheme.substr(0, scheme.length - 1))) {
      res.set('backend-type', scheme.substr(0, scheme.length - 1));
      res.set('--use-ssl', 'true');
    }

    return res;
  }
  encodeDictAsUrl(dict?: Record<string, string>): string {
    if (dict == null) {
      return '';
    }
    let list: string[] = [];
    for (let p in dict) {
      let x = encodeURIComponent(p);
      if (dict[p] != null) {
        x += '=' + encodeURIComponent(dict[p]);
      }
      list.push(x);
    }
    if (list.length === 0) {
      return '';
    }
    return '?' + list.join('&');
  }
}
