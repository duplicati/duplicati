import { ComponentFixture, TestBed } from '@angular/core/testing';

import { WaitAreaComponent } from './wait-area.component';

describe('WaitAreaComponent', () => {
  let component: WaitAreaComponent;
  let fixture: ComponentFixture<WaitAreaComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [WaitAreaComponent]
    });
    fixture = TestBed.createComponent(WaitAreaComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
