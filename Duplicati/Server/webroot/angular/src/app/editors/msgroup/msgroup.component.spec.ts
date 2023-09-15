import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MsgroupComponent } from './msgroup.component';

describe('MsgroupComponent', () => {
  let component: MsgroupComponent;
  let fixture: ComponentFixture<MsgroupComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [MsgroupComponent]
    });
    fixture = TestBed.createComponent(MsgroupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
