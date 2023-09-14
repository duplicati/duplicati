import { TestBed } from '@angular/core/testing';

import { RestoreService } from './restore.service';

describe('RestoreService', () => {
  let service: RestoreService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(RestoreService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
