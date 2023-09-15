import { ComponentFixture, TestBed } from '@angular/core/testing';

import { GenericComponent } from './generic.component';

describe('GenericComponent', () => {
  let component: GenericComponent;
  let fixture: ComponentFixture<GenericComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [GenericComponent]
    });
    fixture = TestBed.createComponent(GenericComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
