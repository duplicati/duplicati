import { getNumberOfCurrencyDigits } from '@angular/common';
import { Component, EventEmitter, Input, IterableChangeRecord, IterableDiffer, IterableDiffers, Output, SimpleChange, SimpleChanges } from '@angular/core';
import { AbstractControl, FormArray, FormBuilder, FormControl, FormGroup, Validators } from '@angular/forms';
import { GroupedOptions, GroupedOptionService } from '../services/grouped-option.service';
import { ParserService } from '../services/parser.service';
import { CommandLineArgument } from '../system-info/system-info';

@Component({
  selector: 'app-advanced-options-editor',
  templateUrl: './advanced-options-editor.component.html',
  styleUrls: ['./advanced-options-editor.component.less']
})
export class AdvancedOptionsEditorComponent {
  @Input({ required: true }) options!: string[];
  @Output() optionsChange = new EventEmitter<string[]>();
  @Input({ required: true }) optionList!: CommandLineArgument[];

  form = this.fb.group({
    items: this.fb.array([]),
    newItem: ['']
  });

  optionListGrouped: GroupedOptions<CommandLineArgument> = [];

  // Overrides to display a custom layout for a specific option
  private overrides: Record<string, string> = {
    'throttle-upload': 'speed',
    'throttle-download': 'speed',

    'retry-delay': 'shorttimespan',
    'web-timeout': 'shorttimespan',
    'run-script-timeout': 'shorttimespan'
  };
  fileSizeMultipliers!: ({ name: string, value: string })[];
  timerangeMultipliers!: ({ name: string, value: string })[];
  speedMultipliers!: ({ name: string, value: string })[];
  shorttimerangeMultipliers!: ({ name: string, value: string })[];
  private optionsMap = new Map<string, CommandLineArgument>();
  private optionsDiffer?: IterableDiffer<string>;

  // Option names of form elements in items
  private itemNames: string[] = [];
  get items(): FormArray {
    return this.form.get('items') as FormArray;
  }

  constructor(private fb: FormBuilder,
    private parser: ParserService,
    private differs: IterableDiffers,
    private groupService: GroupedOptionService) { }

  ngOnChanges(changes: SimpleChanges) {
    if ('optionList' in changes) {
      this.updateOptionList();
    }
    if ('options' in changes) {
      this.updateOptionsForm();
    }
  }

  ngOnInit() {
    this.fileSizeMultipliers = this.parser.fileSizeMultipliers;
    this.timerangeMultipliers = this.parser.timerangeMultipliers;
    this.speedMultipliers = this.parser.speedMultipliers;
    this.shorttimerangeMultipliers = this.parser.shorttimerangeMultipliers;

    let newItem = this.form.get('newItem') as FormControl;
    newItem.valueChanges.subscribe(v => {
      if (typeof v === 'string' && v !== '') {
        // Add new option
        this.addNewOption(v);
        newItem.setValue('');
      }
    });
  }

  private updateOptionsForm(): void {
    if (!this.optionsDiffer) {
      this.optionsDiffer = this.differs.find(this.options).create();
    }
    const changes = this.optionsDiffer.diff(this.options);
    if (changes) {
      changes.forEachOperation((item: IterableChangeRecord<string>, previousIndex: number | null, currentIndex: number | null) => {
        if (previousIndex == null) {
          // New item needs to be inserted
          if (currentIndex == null) {
            currentIndex = this.items.length;
          }
          let [control, name] = this.createFormControl(currentIndex);
          this.items.setControl(currentIndex, control);
          this.itemNames[currentIndex] = name;
        } else if (currentIndex == null) {
          // Item needs to be removed
          if (previousIndex == null) {
            previousIndex = this.items.length - 1;
          }
          this.items.removeAt(previousIndex);
          this.itemNames.splice(previousIndex, 1);
        } else if (previousIndex !== null) {
          // Item needs to be moved
          let control = this.items.at(previousIndex);
          let name = this.itemNames[previousIndex];
          this.items.removeAt(previousIndex);
          this.itemNames.splice(previousIndex, 1);
          this.items.insert(currentIndex, control);
          this.itemNames.splice(currentIndex, 0, name);
        }
      });
    }
  }
  private createFormControl(i: number): [AbstractControl, string] {
    let type = this.getInputType(i);
    let control: FormControl | FormGroup;
    let name: string;
    if (type === 'text' || type === 'password') {
      let parsed = this.parser.parseOptionString(this.options[i]);
      name = parsed.name;
      control = this.fb.control(parsed.value, { updateOn: 'blur' });
    } else if (type === 'enum') {
      let parsed = this.parser.parseOptionEnum(this.options[i], this.getEnumerations(i));
      name = parsed.name;
      control = this.fb.control(parsed.value, { updateOn: 'blur' });
    } else if (type === 'flags') {
      let parsed = this.parser.parseOptionFlags(this.options[i], this.getEnumerations(i));
      name = parsed.name;
      control = this.fb.control(parsed.value, { updateOn: 'blur' });
    } else if (type === 'bool') {
      let parsed = this.parser.parseOptionBool(this.options[i]);
      name = parsed.name;
      control = this.fb.control(parsed.value, { updateOn: 'blur' });
    } else if (type === 'int') {
      let parsed = this.parser.parseOptionInteger(this.options[i]);
      name = parsed.name;
      control = this.fb.control(parsed.value, { updateOn: 'blur' });
    } else if (type === 'size' || type === 'speed' || type === 'timespan' || type === 'shorttimespan') {
      const multCase = (type === 'timespan' || type === 'shorttimespan') ? undefined : 'uppercase';
      let parsed = this.parser.parseOptionSize(this.options[i], multCase);
      name = parsed.name;
      control = this.fb.group({
        value: [parsed.value],
        unit: [parsed.multiplier]
      });
    } else {
      // Default to text
      let parsed = this.parser.parseOptionString(this.options[i]);
      name = parsed.name;
      control = this.fb.control(parsed.value || '');
    }

    if (control instanceof FormControl) {
      control.valueChanges.subscribe(v => {
        this.options[i] = name + '=' + v || '';
      });
    } else if (control instanceof FormGroup) {
      control.valueChanges.subscribe(v => {
        let value = v.value;
        let unit = v.unit;
        this.options[i] = name + '=' + (value || '0') + (unit || '');
      });
    }
    return [control, name];
  }
  private updateOptionList(): void {
    // Group by category and order by name
    this.optionListGrouped = this.groupService.groupOptions(this.optionList,
      v => v.Category || '');
    
    for (let group of this.optionListGrouped) {
      group.value.sort();
    }
    this.optionsMap.clear();
    this.optionList.forEach(option => {
      this.optionsMap.set(option.Name.toLowerCase(), option);
    });
  }

  private getCoreName(key: string | CommandLineArgument | undefined): string {
    let name;
    if (key == null) {
      return '';
    } else if (typeof key !== 'string') {
      name = key.Name;
    } else {
      name = key;
    }
    if (name.startsWith('--')) {
      name = name.substr(2);
    }
    let eqPos = name.indexOf('=');
    if (eqPos >= 0) {
      name = name.substr(0, eqPos);
    }
    return name;
  }

  getEntry(key: string | CommandLineArgument): CommandLineArgument | undefined {
    return this.optionsMap.get(this.getCoreName(key).toLowerCase());
  }

  getInputType(i: number): string {
    let item = this.getEntry(this.options[i]);
    if (item == null) {
      return 'text';
    }

    if (this.overrides[item.Name]) {
      return this.overrides[item.Name];
    }
    if (item.Type === 'Enumeration') {
      return 'enum';
    } else if (item.Type === 'Flags') {
      return 'flags';
    } else if (item.Type === 'Boolean') {
      return 'bool';
    } else if (item.Type === 'Password') {
      return 'password';
    } else if (item.Type === 'Integer') {
      return 'int';
    } else if (item.Type === 'Size') {
      return 'size';
    } else if (item.Type === 'Timespan') {
      return 'timespan';
    } else {
      return 'text';
    }
  }
  getShortName(i: number): string {
    let item = this.getEntry(this.options[i]);
    if (item == null) {
      return this.getCoreName(this.options[i]);
    }
    return item.Name;
  }
  getShortDescription(i: number): string | undefined {
    let item = this.getEntry(this.options[i]);
    return item?.ShortDescription;
  }
  getLongDescription(i: number): string | undefined {
    let item = this.getEntry(this.options[i]);
    return item?.LongDescription;
  }
  getDefaultValue(i: number): string | undefined {
    let item = this.getEntry(this.options[i]);
    return item?.DefaultValue || undefined;
  }
  getEnumerations(i: number): string[] | undefined {
    let item = this.getEntry(this.options[i]);
    return item?.ValidValues || undefined;
  }
  getDeprecationMessage(item: number): string | undefined {
    return this.getEntry(this.options[item])?.DeprecationMessage;
  }
  getDisplayName(item: CommandLineArgument): string {
    let name = item.Name;
    if (item.Deprecated) {
      name += ' (DEPRECATED)';
    }
    return name + ': ' + item.ShortDescription;
  }
  deleteItem(i: number) {
    this.options.splice(i);
    this.optionsChange.next(this.options);
    this.updateOptionsForm();
  }
  addNewOption(key: string) {
    let opt = '--' + key;
    let item = this.getEntry(opt);
    if (item != null) {
      opt += '=' + item.DefaultValue;
    }
    this.options.push(opt);
    this.optionsChange.next(this.options);
    this.updateOptionsForm();
  }
}
