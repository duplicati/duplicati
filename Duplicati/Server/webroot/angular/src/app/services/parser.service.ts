import { Injectable } from '@angular/core';
import { CommandLineArgument, SystemInfo } from '../system-info/system-info';
import { ConvertService } from './convert.service';

@Injectable({
  providedIn: 'root'
})
export class ParserService {

  exampleOptionString = '--dblock-size=100MB';

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

  serializeAdvancedOptionsToArray(options: Record<string, string>): string[] {
    let res: string[] = [];
    for (let k in options) {
      if (k.indexOf('--') == 0) {
        res.push(k + '=' + options[k]);
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

  buildOptionList(sysInfo: SystemInfo, encmodule: boolean | string, compmodule: boolean | string, backmodule: boolean): CommandLineArgument[] {
    if (sysInfo == null || sysInfo.Options == null) {
      return [];
    }

    let items = structuredClone(sysInfo.Options);
    for (let item of items) {
      item.Category = 'Core options';
    }

    let copyToList = (lst: any[], key?: string | boolean) => {
      if (typeof key != 'string') {
        key = undefined;
      }

      for (let e of lst) {
        if (key == null || key.toLowerCase() == e.Key.toLowerCase()) {
          let m = structuredClone(e.Options);
          for (let item of m) {
            item.Category = e.DisplayName;
          }
          items.push.apply(items, m);
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
}
