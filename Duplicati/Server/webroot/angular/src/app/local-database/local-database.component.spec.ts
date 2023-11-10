import { ComponentFixture, TestBed } from '@angular/core/testing';

import { LocalDatabaseComponent } from './local-database.component';

describe('LocalDatabaseComponent', () => {
  let component: LocalDatabaseComponent;
  let fixture: ComponentFixture<LocalDatabaseComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [LocalDatabaseComponent]
    });
    fixture = TestBed.createComponent(LocalDatabaseComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
