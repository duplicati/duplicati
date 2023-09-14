import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RestoreWizardComponent } from './restore-wizard.component';

describe('RestoreWizardComponent', () => {
  let component: RestoreWizardComponent;
  let fixture: ComponentFixture<RestoreWizardComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [RestoreWizardComponent]
    });
    fixture = TestBed.createComponent(RestoreWizardComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
