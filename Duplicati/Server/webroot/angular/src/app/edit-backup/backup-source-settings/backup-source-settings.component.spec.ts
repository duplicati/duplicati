import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupSourceSettingsComponent } from './backup-source-settings.component';

describe('BackupSourceSettingsComponent', () => {
  let component: BackupSourceSettingsComponent;
  let fixture: ComponentFixture<BackupSourceSettingsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupSourceSettingsComponent]
    });
    fixture = TestBed.createComponent(BackupSourceSettingsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
