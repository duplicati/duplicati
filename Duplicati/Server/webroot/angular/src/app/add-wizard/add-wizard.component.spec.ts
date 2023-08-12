import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AddWizardComponent } from './add-wizard.component';

describe('AddWizardComponent', () => {
  let component: AddWizardComponent;
  let fixture: ComponentFixture<AddWizardComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [AddWizardComponent]
    });
    fixture = TestBed.createComponent(AddWizardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
