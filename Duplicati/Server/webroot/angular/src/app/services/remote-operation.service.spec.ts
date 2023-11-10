import { TestBed } from '@angular/core/testing';

import { RemoteOperationService } from './remote-operation.service';

describe('RemoteOperationService', () => {
  let service: RemoteOperationService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(RemoteOperationService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
