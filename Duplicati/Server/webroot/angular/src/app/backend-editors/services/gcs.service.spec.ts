import { TestBed } from '@angular/core/testing';

import { GcsService } from './gcs.service';

describe('GcsService', () => {
  let service: GcsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(GcsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
