export interface DialogCallback {
  (buttonIndex: number, input: string | undefined, dialog: DialogConfig): void;
}

export interface DialogConfig {
  message?: string;
  callback?: DialogCallback;
  title?: string;
  buttons?: string[];
  dismiss?: () => void;
  onshow?: () => void;
  ondismiss?: () => void;

  enableTextarea?: boolean;
  placeholder?: string;
  textarea?: string;

  htmltemplate?: string;
  buttonTemplate?: string;
}

export interface DialogState {
  CurrentItem?: DialogConfig;
  Queue: DialogConfig[];
}
