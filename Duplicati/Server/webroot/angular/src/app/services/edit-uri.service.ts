import { inject, Inject, Injectable, Type } from '@angular/core';
import { BackendEditorComponent, BACKEND_EDITORS } from '../editors/backend-editor.component';

@Injectable({
  providedIn: 'root'
})
export class EditUriService {
  private editors = new Map<string, Type<BackendEditorComponent>>();

  constructor(@Inject(BACKEND_EDITORS) editorTypes: ({ key: string, type: Type<BackendEditorComponent> })[]) {
    for (let e of editorTypes) {
      this.editors.set(e.key, e.type);
    }
  }

  get defaultbackend(): string {
    return 'file';
  }

  getEditorType(key: string): Type<BackendEditorComponent> | undefined {
    return this.editors.get(key);
  }
}
