import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupScheduleComponent } from './backup-schedule.component';

describe('BackupScheduleComponent', () => {
  let component: BackupScheduleComponent;
  let fixture: ComponentFixture<BackupScheduleComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupScheduleComponent]
    });
    fixture = TestBed.createComponent(BackupScheduleComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
