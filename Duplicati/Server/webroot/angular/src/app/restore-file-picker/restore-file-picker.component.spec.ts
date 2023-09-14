import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RestoreFilePickerComponent } from './restore-file-picker.component';

describe('RestoreFilePickerComponent', () => {
  let component: RestoreFilePickerComponent;
  let fixture: ComponentFixture<RestoreFilePickerComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [RestoreFilePickerComponent]
    });
    fixture = TestBed.createComponent(RestoreFilePickerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
