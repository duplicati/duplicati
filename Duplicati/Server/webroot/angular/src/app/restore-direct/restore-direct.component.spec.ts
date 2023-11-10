import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RestoreDirectComponent } from './restore-direct.component';

describe('RestoreDirectComponent', () => {
  let component: RestoreDirectComponent;
  let fixture: ComponentFixture<RestoreDirectComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [RestoreDirectComponent]
    });
    fixture = TestBed.createComponent(RestoreDirectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
