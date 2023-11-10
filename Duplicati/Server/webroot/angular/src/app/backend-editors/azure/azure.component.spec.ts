import { ComponentFixture, TestBed } from '@angular/core/testing';

import { AzureComponent } from './azure.component';

describe('AzureComponent', () => {
  let component: AzureComponent;
  let fixture: ComponentFixture<AzureComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [AzureComponent]
    });
    fixture = TestBed.createComponent(AzureComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
