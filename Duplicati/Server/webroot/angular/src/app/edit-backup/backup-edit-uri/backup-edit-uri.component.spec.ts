import { ComponentFixture, TestBed } from '@angular/core/testing';

import { BackupEditUriComponent } from './backup-edit-uri.component';

describe('BackupEditUriComponent', () => {
  let component: BackupEditUriComponent;
  let fixture: ComponentFixture<BackupEditUriComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [BackupEditUriComponent]
    });
    fixture = TestBed.createComponent(BackupEditUriComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
