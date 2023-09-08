import { TestBed } from '@angular/core/testing';

import { BackupDefaultsService } from './backup-defaults.service';

describe('BackupDefaultsService', () => {
  let service: BackupDefaultsService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(BackupDefaultsService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
