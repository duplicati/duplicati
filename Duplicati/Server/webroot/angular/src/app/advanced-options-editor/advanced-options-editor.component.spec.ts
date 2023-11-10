import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AdvancedOptionsEditorComponent } from './advanced-options-editor.component';

describe('AdvancedOptionsEditorComponent', () => {
  let component: AdvancedOptionsEditorComponent;
  let fixture: ComponentFixture<AdvancedOptionsEditorComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [AdvancedOptionsEditorComponent]
    });
    fixture = TestBed.createComponent(AdvancedOptionsEditorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
