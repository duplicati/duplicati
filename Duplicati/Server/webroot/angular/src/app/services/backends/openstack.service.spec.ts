import { TestBed } from '@angular/core/testing';

import { OpenstackService } from './openstack.service';

describe('OpenstackService', () => {
  let service: OpenstackService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(OpenstackService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
