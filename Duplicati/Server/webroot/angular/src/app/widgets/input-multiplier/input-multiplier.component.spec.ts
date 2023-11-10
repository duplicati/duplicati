import { ComponentFixture, TestBed } from '@angular/core/testing';

import { InputMultiplierComponent } from './input-multiplier.component';

describe('InputMultiplierComponent', () => {
  let component: InputMultiplierComponent;
  let fixture: ComponentFixture<InputMultiplierComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [InputMultiplierComponent]
    });
    fixture = TestBed.createComponent(InputMultiplierComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
