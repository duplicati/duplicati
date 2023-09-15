import { ComponentFixture, TestBed } from '@angular/core/testing';

import { S3Component } from './s3.component';

describe('S3Component', () => {
  let component: S3Component;
  let fixture: ComponentFixture<S3Component>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [S3Component]
    });
    fixture = TestBed.createComponent(S3Component);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
