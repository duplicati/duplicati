import { formatDate } from '@angular/common';
import { EventEmitter, Input, Output } from '@angular/core';
import { Component } from '@angular/core';
import { Schedule } from '../../backup';
import { ConvertService } from '../../services/convert.service';
import { ParserService } from '../../services/parser.service';

@Component({
  selector: 'app-backup-schedule',
  templateUrl: './backup-schedule.component.html',
  styleUrls: ['./backup-schedule.component.less']
})
export class BackupScheduleComponent {
  private _schedule: Schedule | null = null;
  @Input() get schedule(): Schedule | null {
    return this._schedule;
  }
  set schedule(value: Schedule | null) {
    if (this._schedule !== value) {
      this._schedule = value;
      this.repeatRun = value?.Repeat || '';
      this.parseTime(value?.Time);
    }
  }
  @Output() scheduleChange = new EventEmitter<Schedule | null>();
  @Output() next = new EventEmitter<void>();
  @Output() prev = new EventEmitter<void>();

  private oldSchedule: Schedule | null = null;

  private _repeatRun: string = '';
  private _repeatRunNumber: number | null = null;
  private _repeatRunMultiplier: string = '';
  get repeatRun(): string {
    return this._repeatRun;
  }
  set repeatRun(value: string) {
    if (this._repeatRun !== value) {
      this._repeatRun = value;
      if (value.length > 0) {
        let res = this.parser.splitSizeString(value || '');
        this._repeatRunNumber = res[0];
        if (this.isTimerangeMultiplier(res[1] || '')) {
          this._repeatRunMultiplier = res[1] || '';
        } else {
          this._repeatRunMultiplier = '';
        }
      } else {
        this._repeatRunNumber = null;
        this._repeatRunMultiplier = '';
      }
      if (this.schedule && this.schedule.Repeat !== value) {
        this.schedule = { ...this.schedule, Repeat: value };
        this.scheduleChange.emit(this.schedule);
      }
    }
  }
  get repeatRunNumber(): number | null {
    return this._repeatRunNumber;
  }
  set repeatRunNumber(value: number | null) {
    if (this._repeatRunNumber !== value) {
      this._repeatRunNumber = value;
      this.repeatRun = (value || '0') + this.repeatRunMultiplier;
    }
  }
  get repeatRunMultiplier(): string {
    return this._repeatRunMultiplier;
  }
  set repeatRunMultiplier(value: string) {
    if (this._repeatRunMultiplier !== value) {
      this._repeatRunMultiplier = value;
      this.repeatRun = (this.repeatRunNumber || '0') + value;
    }
  }


  get scheduleEnabled(): boolean {
    return this.schedule != null;
  }
  set scheduleEnabled(v: boolean) {
    if (!v) {
      this.oldSchedule = this.schedule;
      this.schedule = null;
      this.scheduleChange.emit(null);
    } else {
      if (this.oldSchedule == null) {
        this.oldSchedule = this.defaultSchedule();
      }
      this.schedule = this.oldSchedule;
      this.oldSchedule = null;
      this.scheduleChange.emit(this.schedule);
    }
  }
  private _scheduleTime?: string;
  private _scheduleDate?: string;
  get scheduleTime(): string | undefined {
    return this._scheduleTime;
  }
  set scheduleTime(v: string | undefined) {
    this._scheduleTime = v;
    this.updateTime();
  }
  get scheduleDate(): string | undefined {
    return this._scheduleDate;
  }
  set scheduleDate(value: string | undefined) {
    this._scheduleDate = value;
    this.updateTime();
  }


  daysOfWeek: ({ name: string, value: string })[] = [];
  timerangeMultipliers: ({ name: string, value: string })[] = [];

  constructor(private parser: ParserService,
    private convert: ConvertService) { }

  ngOnInit() {
    this.daysOfWeek = this.parser.daysOfWeek;
    this.timerangeMultipliers = this.parser.timerangeMultipliers;
  }

  private defaultSchedule(): Schedule {
    const now = new Date();
    let nextTime = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 13, 0, 0);
    if (nextTime < now) {
      nextTime.setDate(nextTime.getDate() + 1);
    }
    return {
      Tags: [],
      Repeat: '1D',
      AllowedDays: this.daysOfWeek.map(v => v.value),
      Time: nextTime.toISOString()
    };
  }

  isTimerangeMultiplier(mult?: string): boolean {
    if (mult === undefined) {
      mult = this.repeatRunMultiplier;
    }
    return this.timerangeMultipliers.findIndex(m => m.value === mult) >= 0;
  }

  isDayAllowed(day: string): boolean {
    return this.schedule != null && this.schedule.AllowedDays.includes(day);
  }
  setDayAllowed(day: string, allowed: boolean): void {
    if (this.schedule == null) {
      return;
    }
    let idx = this.schedule.AllowedDays.indexOf(day);
    if (idx >= 0 && !allowed) {
      let copy = [...this.schedule.AllowedDays];
      copy.splice(idx, 1);
      this.schedule = { ...this.schedule, AllowedDays: copy };
    } else if (idx < 0 && allowed) {
      let copy = [...this.schedule.AllowedDays];
      copy.push(day);
      this.schedule = { ...this.schedule, AllowedDays: copy };
    }
  }
  private updateTime(): void {
    const str = (this.scheduleDate || '') + 'T' + (this.scheduleTime || '');
    if (this.schedule && this.schedule.Time !== str) {
      this.schedule = { ...this.schedule, Time: str };
      this.scheduleChange.emit(this.schedule);
    }
  }

  private parseTime(time: string | undefined): void {
    if (time == null) {
      this._scheduleDate = undefined;
      this._scheduleDate = undefined;
    } else {
      let parsed = this.convert.parseDate(time);
      this._scheduleDate = formatDate(parsed, 'yyyy-MM-dd', 'en-US');
      this._scheduleTime = formatDate(parsed, 'HH:mm:ss', 'en-US');
    }
  }
}
