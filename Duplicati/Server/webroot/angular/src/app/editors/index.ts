import { BACKEND_EDITORS } from "./backend-editor";
import { EditFileComponent } from "./edit-file/edit-file.component";

export const backendEditorProviders = [
  { provide: BACKEND_EDITORS, useValue: { key: 'file', type: EditFileComponent }, multi: true },
];
