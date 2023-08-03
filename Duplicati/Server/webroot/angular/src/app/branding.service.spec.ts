import { TestBed } from '@angular/core/testing';

import { BrandingService } from './branding.service';

describe('BrandingService', () => {
  let service: BrandingService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(BrandingService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
