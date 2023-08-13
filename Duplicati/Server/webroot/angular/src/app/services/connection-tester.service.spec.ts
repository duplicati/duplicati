import { TestBed } from '@angular/core/testing';

import { ConnectionTester } from './connection-tester.service';

describe('ConnectionTesterService', () => {
  let service: ConnectionTester;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ConnectionTester);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
