import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UpdateChangelogComponent } from './update-changelog.component';

describe('UpdateChangelogComponent', () => {
  let component: UpdateChangelogComponent;
  let fixture: ComponentFixture<UpdateChangelogComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [UpdateChangelogComponent]
    });
    fixture = TestBed.createComponent(UpdateChangelogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
