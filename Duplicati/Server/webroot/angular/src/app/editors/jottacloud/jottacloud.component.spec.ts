import { ComponentFixture, TestBed } from '@angular/core/testing';

import { JottacloudComponent } from './jottacloud.component';

describe('JottacloudComponent', () => {
  let component: JottacloudComponent;
  let fixture: ComponentFixture<JottacloudComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [JottacloudComponent]
    });
    fixture = TestBed.createComponent(JottacloudComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
