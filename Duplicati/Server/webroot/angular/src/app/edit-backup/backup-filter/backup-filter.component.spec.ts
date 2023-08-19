import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupFiltersComponent } from './backup-filters.component';

describe('BackupFiltersComponent', () => {
  let component: BackupFiltersComponent;
  let fixture: ComponentFixture<BackupFiltersComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupFiltersComponent]
    });
    fixture = TestBed.createComponent(BackupFiltersComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
