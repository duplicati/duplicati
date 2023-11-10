import { TestBed } from '@angular/core/testing';

import { ServerStatusService } from './server-status.service';

describe('ServerStatusService', () => {
  let service: ServerStatusService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ServerStatusService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
