import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EditBackupComponent } from './edit-backup.component';

describe('EditBackupComponent', () => {
  let component: EditBackupComponent;
  let fixture: ComponentFixture<EditBackupComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [EditBackupComponent]
    });
    fixture = TestBed.createComponent(EditBackupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
