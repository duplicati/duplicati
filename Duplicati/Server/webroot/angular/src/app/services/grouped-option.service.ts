import { Injectable } from '@angular/core';

export type GroupedOptions<T> = ({ key: string, value: T[] })[];

@Injectable({
  providedIn: 'root'
})
export class GroupedOptionService {

  constructor() { }

  compareFields<T>(...fn: ((v: T) => any)[]): (v1:T,v2:T)=>number{
    return (v1: T, v2: T): number => {
      for (let f of fn) {
        let res = this.compare(f(v1), f(v2));
        if (res !== 0) {
          return res;
        }
      }
      return 0;
    };
  }

  compare<T>(v1: T, v2: T): number {
    if (v1> v2) {
      return 1;
    } else if (v1 < v2) {
      return -1;
    }
    return 0;
  }

  groupOptions<T>(options: T[] | undefined, groupBy: (v: T) => string, orderBy?: (v1: T, v2: T) => number): GroupedOptions<T> {
    if (options == null) {
      return [];
    }
    if (orderBy !== undefined) {
      options = options.slice();
      options.sort(orderBy);
    }
    let grouped: GroupedOptions<T> = [];
    let groupMap = new Map<string, T[]>();
    for (const opt of options) {
      const key = groupBy(opt);
      if (groupMap.has(key)) {
        groupMap.get(key)!.push(opt);
      } else {
        let value = [opt];
        grouped.push({ key: key, value });
        groupMap.set(key, value);
      }
    }

    return grouped;
  }
}
