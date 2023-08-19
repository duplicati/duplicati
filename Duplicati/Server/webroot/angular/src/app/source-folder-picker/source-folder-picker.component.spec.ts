import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SourceFolderPickerComponent } from './source-folder-picker.component';

describe('SourceFolderPickerComponent', () => {
  let component: SourceFolderPickerComponent;
  let fixture: ComponentFixture<SourceFolderPickerComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [SourceFolderPickerComponent]
    });
    fixture = TestBed.createComponent(SourceFolderPickerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
