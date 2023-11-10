import { ComponentFixture, TestBed } from '@angular/core/testing';

import { NotificationAreaComponent } from './notification-area.component';

describe('NotificationAreaComponent', () => {
  let component: NotificationAreaComponent;
  let fixture: ComponentFixture<NotificationAreaComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [NotificationAreaComponent]
    });
    fixture = TestBed.createComponent(NotificationAreaComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
