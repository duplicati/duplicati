import { Directive, ElementRef, Inject, Optional, Renderer2 } from '@angular/core';
import { COMPOSITION_BUFFER_MODE, ControlValueAccessor, DefaultValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { ConvertService } from '../services/convert.service';
import { ParserService } from '../services/parser.service';

// Convert a string[] to multiple lines of text
@Directive({
  selector: '[appStringArrayText]',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: StringArrayTextDirective, multi: true }]
})
export class StringArrayTextDirective extends DefaultValueAccessor {

  constructor(renderer: Renderer2,
    elementRef: ElementRef,
    @Optional() @Inject(COMPOSITION_BUFFER_MODE) compositionMode: boolean,
    private convert: ConvertService) {
    super(renderer, elementRef, compositionMode);
  }

  // Wrap a string[] to multiple lines
  override writeValue(obj: any): void {
    let value = (obj || []).join('\n');
    super.writeValue(value);
  }
  override registerOnChange(fn: any): void {
    if (fn == null) {
      this.onChange = fn;
    } else {
      this.onChange = (val: any) => {
        // Convert val from string to array
        let arr = this.convert.removeEmptyEntries(
          this.convert.replaceAll(val || '' as string, '\r', '\n').split('\n'));
        fn(arr);
      }
    }
  }
}
