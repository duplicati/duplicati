import { EventEmitter, Output } from '@angular/core';
import { SimpleChanges } from '@angular/core';
import { booleanAttribute } from '@angular/core';
import { Component, Input } from '@angular/core';
import { ParserService } from '../../services/parser.service';

@Component({
  selector: 'app-input-multiplier',
  templateUrl: './input-multiplier.component.html',
  styleUrls: ['./input-multiplier.component.less']
})
export class InputMultiplierComponent {
  @Input({ required: true }) name!: string;
  @Input({ required: true }) multipliers!: ({ name: string, value: string })[];
  @Input({ transform: booleanAttribute }) hasCustom: boolean = false;
  @Input() case: string = '';
  @Input({ alias: 'value' }) set valueInput(value: string) {
    this._value = value;
  }
  @Output() valueChange = new EventEmitter<string>();

  private _value: string = '';
  private _number: number | null = null;
  private _multiplier: string = '';
  get value(): string {
    return this._value;
  }
  set value(value: string) {
    if (this._value !== value) {
      this._value = value;
      this.updateFields(value);
      this.valueChange.emit(value);
    }
  }
  get number(): number | null {
    return this._number;
  }
  set number(value: number | null) {
    if (this._number !== value) {
      this._number = value;
      this.value = (value || '0') + this.multiplier;
    }
  }
  get multiplier(): string {
    return this._multiplier;
  }
  set multiplier(value: string) {
    value = this.transformCase(value);
    if (this._multiplier !== value) {
      this._multiplier = value;
      this.value = (this.number || '0') + value;
    }
  }

  constructor(private parser: ParserService) { }

  ngOnChanges(changes: SimpleChanges) {
    this.updateFields(this._value);
  }

  showCustomInput(): boolean {
    return this.hasCustom && !this.isValidMultiplier(this.multiplier);
  }

  transformCase(str: string): string {
    if (this.case === 'uppercase') {
      return str.toUpperCase();
    } else if (this.case === 'lowercase') {
      return str.toLowerCase();
    }
    return str;
  }

  isValidMultiplier(mult: string): boolean {
    return this.multipliers.findIndex(m => this.transformCase(m.value) === this.transformCase(mult)) >= 0;
  }

  private updateFields(value: string) {
    if (value.length > 0) {
      const res = this.parser.splitSizeString(value || '');
      this._number = res[0];
      if (this.isValidMultiplier(res[1] || '')) {
        this._multiplier = this.transformCase(res[1] || '');
      } else {
        this._multiplier = '';
      }
    } else {
      this._number = null;
      this._multiplier = '';
    }
  }

}
