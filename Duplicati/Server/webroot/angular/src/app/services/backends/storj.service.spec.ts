import { TestBed } from '@angular/core/testing';

import { StorjService } from './storj.service';

describe('StorjService', () => {
  let service: StorjService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(StorjService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
