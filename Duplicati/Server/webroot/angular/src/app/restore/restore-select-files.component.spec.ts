import { ComponentFixture, TestBed } from '@angular/core/testing';

import { RestoreSelectFilesComponent } from './restore-select-files.component';

describe('RestoreSelectFilesComponent', () => {
  let component: RestoreSelectFilesComponent;
  let fixture: ComponentFixture<RestoreSelectFilesComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [RestoreSelectFilesComponent]
    });
    fixture = TestBed.createComponent(RestoreSelectFilesComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
