import { TestBed } from '@angular/core/testing';

import { BackupListService } from './backup-list.service';

describe('BackupListService', () => {
  let service: BackupListService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(BackupListService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
