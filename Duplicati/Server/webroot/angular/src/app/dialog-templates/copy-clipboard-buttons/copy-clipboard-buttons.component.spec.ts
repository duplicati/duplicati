import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CopyClipboardButtonsComponent } from './copy-clipboard-buttons.component';

describe('CopyClipboardButtonsComponent', () => {
  let component: CopyClipboardButtonsComponent;
  let fixture: ComponentFixture<CopyClipboardButtonsComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [CopyClipboardButtonsComponent]
    });
    fixture = TestBed.createComponent(CopyClipboardButtonsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
