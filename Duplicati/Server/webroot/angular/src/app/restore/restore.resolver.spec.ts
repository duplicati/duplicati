import { TestBed } from '@angular/core/testing';
import { ResolveFn } from '@angular/router';

import { restoreResolver } from './restore.resolver';

describe('restoreResolver', () => {
  const executeResolver: ResolveFn<boolean> = (...resolverParameters) => 
      TestBed.runInInjectionContext(() => restoreResolver(...resolverParameters));

  beforeEach(() => {
    TestBed.configureTestingModule({});
  });

  it('should be created', () => {
    expect(executeResolver).toBeTruthy();
  });
});
