import { TestBed } from '@angular/core/testing';

import { ServerSettingsService } from './server-settings.service';

describe('ServerSettingsService', () => {
  let service: ServerSettingsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ServerSettingsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
