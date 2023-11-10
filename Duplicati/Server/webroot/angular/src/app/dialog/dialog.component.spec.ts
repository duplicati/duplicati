import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DialogComponent } from './dialog.component';

describe('DialogComponent', () => {
  let component: DialogComponent;
  let fixture: ComponentFixture<DialogComponent>;

  beforeEach(() => {
    TestBed.configureTestingModule({
      declarations: [DialogComponent]
    });
    fixture = TestBed.createComponent(DialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  //it('should show dialogs', () => {
  //setTimeout(() => this.dialogService.alert('Test alert'), 1000);
  //setTimeout(() => this.dialogService.confirm('Test confirm', (idx, input, dialog) => {
  //  this.dialogService.alert(`Confirmed ${idx}`);
  //}), 1000);
  //setTimeout(() => this.dialogService.accept('Test accept', (idx, input, dialog) => {
  //  this.dialogService.alert(`Accepted ${idx}`);
  //}), 1000);
  //setTimeout(() => this.dialogService.dialog('Test dialog', 'Dialog message', ['OK', 'Cancel', 'Bla'], (idx, input, dialog) => {
  //  this.dialogService.alert(`Pressed ${idx}`);
  //}, () => this.dialogService.alert("show")), 1000);
  //setTimeout(() => this.dialogService.textareaDialog('Textarea dialog', 'Dialog message', 'Placeholder', '', ['OK', 'Cancel', 'Bla'], 'buttonTemplate', (idx, input, dialog) => {
  //  this.dialogService.alert(`Pressed ${idx} with ${input}`);
  //}), 1000);
  //});
});
