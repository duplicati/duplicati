import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupOptionsComponent } from './backup-options.component';

describe('BackupOptionsComponent', () => {
  let component: BackupOptionsComponent;
  let fixture: ComponentFixture<BackupOptionsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupOptionsComponent]
    });
    fixture = TestBed.createComponent(BackupOptionsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
