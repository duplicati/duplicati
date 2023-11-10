import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CosComponent } from './cos.component';

describe('CosComponent', () => {
  let component: CosComponent;
  let fixture: ComponentFixture<CosComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [CosComponent]
    });
    fixture = TestBed.createComponent(CosComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
