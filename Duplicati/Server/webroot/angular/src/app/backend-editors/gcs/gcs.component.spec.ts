import { ComponentFixture, TestBed } from '@angular/core/testing';

import { GcsComponent } from './gcs.component';

describe('GcsComponent', () => {
  let component: GcsComponent;
  let fixture: ComponentFixture<GcsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [GcsComponent]
    });
    fixture = TestBed.createComponent(GcsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
