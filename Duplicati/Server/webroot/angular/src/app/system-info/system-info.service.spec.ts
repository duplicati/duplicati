import { TestBed } from '@angular/core/testing';

import { SystemInfoService } from './system-info.service';

describe('SystemInfoService', () => {
  let service: SystemInfoService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(SystemInfoService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
