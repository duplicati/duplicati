import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupGeneralSettingsComponent } from './backup-general-settings.component';

describe('BackupGeneralSettingsComponent', () => {
  let component: BackupGeneralSettingsComponent;
  let fixture: ComponentFixture<BackupGeneralSettingsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupGeneralSettingsComponent]
    });
    fixture = TestBed.createComponent(BackupGeneralSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
