import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ResultEntryComponent } from './result-entry.component';

describe('ResultEntryComponent', () => {
  let component: ResultEntryComponent;
  let fixture: ComponentFixture<ResultEntryComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [ResultEntryComponent]
    });
    fixture = TestBed.createComponent(ResultEntryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
