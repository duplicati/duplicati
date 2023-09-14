import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RestoreLocationComponent } from './restore-location.component';

describe('RestoreLocationComponent', () => {
  let component: RestoreLocationComponent;
  let fixture: ComponentFixture<RestoreLocationComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [RestoreLocationComponent]
    });
    fixture = TestBed.createComponent(RestoreLocationComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
