import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupLogComponent } from './backup-log.component';

describe('BackupLogComponent', () => {
  let component: BackupLogComponent;
  let fixture: ComponentFixture<BackupLogComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupLogComponent]
    });
    fixture = TestBed.createComponent(BackupLogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
