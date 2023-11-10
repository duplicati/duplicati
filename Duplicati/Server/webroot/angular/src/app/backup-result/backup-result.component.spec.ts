import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupResultComponent } from './backup-result.component';

describe('BackupResultComponent', () => {
  let component: BackupResultComponent;
  let fixture: ComponentFixture<BackupResultComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupResultComponent]
    });
    fixture = TestBed.createComponent(BackupResultComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
