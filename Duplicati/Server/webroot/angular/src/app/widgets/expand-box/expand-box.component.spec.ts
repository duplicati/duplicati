import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ExpandBoxComponent } from './expand-box.component';

describe('ExpandBoxComponent', () => {
  let component: ExpandBoxComponent;
  let fixture: ComponentFixture<ExpandBoxComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [ExpandBoxComponent]
    });
    fixture = TestBed.createComponent(ExpandBoxComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
