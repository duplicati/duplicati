import { TestBed } from '@angular/core/testing';

import { S3Service } from './s3.service';

describe('S3Service', () => {
  let service: S3Service;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(S3Service);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
