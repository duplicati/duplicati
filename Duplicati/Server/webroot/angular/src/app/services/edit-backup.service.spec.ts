import { TestBed } from '@angular/core/testing';

import { EditBackupService } from './edit-backup.service';

describe('EditBackupService', () => {
  let service: EditBackupService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(EditBackupService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
