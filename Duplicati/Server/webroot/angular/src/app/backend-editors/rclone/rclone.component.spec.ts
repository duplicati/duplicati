import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RcloneComponent } from './rclone.component';

describe('RcloneComponent', () => {
  let component: RcloneComponent;
  let fixture: ComponentFixture<RcloneComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [RcloneComponent]
    });
    fixture = TestBed.createComponent(RcloneComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
