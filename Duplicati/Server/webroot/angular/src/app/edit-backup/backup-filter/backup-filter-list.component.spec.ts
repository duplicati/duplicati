import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupFilterListComponent } from './backup-filter-list.component';

describe('BackupFilterListComponent', () => {
  let component: BackupFilterListComponent;
  let fixture: ComponentFixture<BackupFilterListComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupFilterListComponent]
    });
    fixture = TestBed.createComponent(BackupFilterListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
