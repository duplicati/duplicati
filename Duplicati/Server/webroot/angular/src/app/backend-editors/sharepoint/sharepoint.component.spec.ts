import { ComponentFixture, TestBed } from '@angular/core/testing';

import { SharepointComponent } from './sharepoint.component';

describe('SharepointComponent', () => {
  let component: SharepointComponent;
  let fixture: ComponentFixture<SharepointComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [SharepointComponent]
    });
    fixture = TestBed.createComponent(SharepointComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
