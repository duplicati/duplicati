import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupTaskComponent } from './backup-task.component';

describe('BackupTaskComponent', () => {
  let component: BackupTaskComponent;
  let fixture: ComponentFixture<BackupTaskComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupTaskComponent]
    });
    fixture = TestBed.createComponent(BackupTaskComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
