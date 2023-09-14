import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'highlight'
})
export class HighlightPipe implements PipeTransform {

  transform(text: string, search: string | RegExp | null, caseSensitive?: boolean): string {
    if (text && search) {
      if (typeof search === 'string') {
        search = new RegExp(search, caseSensitive ? 'g' : 'gi');
      }
      return text.replace(search, '<span class="ui-match">$&</span>');
    }
    return text;
  }

}
