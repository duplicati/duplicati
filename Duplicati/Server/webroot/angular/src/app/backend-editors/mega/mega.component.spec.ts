import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MegaComponent } from './mega.component';

describe('MegaComponent', () => {
  let component: MegaComponent;
  let fixture: ComponentFixture<MegaComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [MegaComponent]
    });
    fixture = TestBed.createComponent(MegaComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
