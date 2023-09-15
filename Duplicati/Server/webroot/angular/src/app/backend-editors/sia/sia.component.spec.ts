import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SiaComponent } from './sia.component';

describe('SiaComponent', () => {
  let component: SiaComponent;
  let fixture: ComponentFixture<SiaComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [SiaComponent]
    });
    fixture = TestBed.createComponent(SiaComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
