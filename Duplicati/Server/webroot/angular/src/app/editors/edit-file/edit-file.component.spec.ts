import { ComponentFixture, TestBed } from '@angular/core/testing';

import { EditFileComponent } from './edit-file.component';

describe('EditFileComponent', () => {
  let component: EditFileComponent;
  let fixture: ComponentFixture<EditFileComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [EditFileComponent]
    });
    fixture = TestBed.createComponent(EditFileComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
