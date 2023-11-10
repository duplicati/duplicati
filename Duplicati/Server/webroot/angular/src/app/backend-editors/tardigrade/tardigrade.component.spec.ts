import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TardigradeComponent } from './tardigrade.component';

describe('TardigradeComponent', () => {
  let component: TardigradeComponent;
  let fixture: ComponentFixture<TardigradeComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [TardigradeComponent]
    });
    fixture = TestBed.createComponent(TardigradeComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
