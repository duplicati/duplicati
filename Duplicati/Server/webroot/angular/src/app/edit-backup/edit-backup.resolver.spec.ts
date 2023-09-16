import { TestBed } from '@angular/core/testing';
import { ResolveFn } from '@angular/router';

import { editBackupResolver } from './edit-backup.resolver';

describe('editBackupResolver', () => {
  const executeResolver: ResolveFn<boolean> = (...resolverParameters) => 
      TestBed.runInInjectionContext(() => editBackupResolver(...resolverParameters));

  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('should be created', () => {
    expect(executeResolver).toBeTruthy();
  });
});
