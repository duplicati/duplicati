import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DestinationFolderPickerComponent } from './destination-folder-picker.component';

describe('DestinationFolderPickerComponent', () => {
  let component: DestinationFolderPickerComponent;
  let fixture: ComponentFixture<DestinationFolderPickerComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [DestinationFolderPickerComponent]
    });
    fixture = TestBed.createComponent(DestinationFolderPickerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
