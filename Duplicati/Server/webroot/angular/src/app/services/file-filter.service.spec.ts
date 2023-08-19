import { TestBed } from '@angular/core/testing';

import { FileFilterService } from './file-filter.service';

describe('FileFilterService', () => {
  let service: FileFilterService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(FileFilterService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
