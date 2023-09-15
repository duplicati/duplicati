import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StorjComponent } from './storj.component';

describe('StorjComponent', () => {
  let component: StorjComponent;
  let fixture: ComponentFixture<StorjComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [StorjComponent]
    });
    fixture = TestBed.createComponent(StorjComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
