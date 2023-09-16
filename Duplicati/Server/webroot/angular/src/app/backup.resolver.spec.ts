import { TestBed } from '@angular/core/testing';
import { ResolveFn } from '@angular/router';

import { backupResolver } from './backup.resolver';

describe('backupResolver', () => {
  const executeResolver: ResolveFn<boolean> = (...resolverParameters) => 
      TestBed.runInInjectionContext(() => backupResolver(...resolverParameters));

  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('should be created', () => {
    expect(executeResolver).toBeTruthy();
  });
});
