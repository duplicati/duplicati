import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupDestinationSettingsComponent } from './backup-destination-settings.component';

describe('BackupDestinationSettingsComponent', () => {
  let component: BackupDestinationSettingsComponent;
  let fixture: ComponentFixture<BackupDestinationSettingsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupDestinationSettingsComponent]
    });
    fixture = TestBed.createComponent(BackupDestinationSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
