import { ComponentFixture, TestBed } from '@angular/core/testing';

import { OpenstackComponent } from './openstack.component';

describe('OpenstackComponent', () => {
  let component: OpenstackComponent;
  let fixture: ComponentFixture<OpenstackComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [OpenstackComponent]
    });
    fixture = TestBed.createComponent(OpenstackComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
