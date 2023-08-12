import { TestBed } from '@angular/core/testing';

import { PassphraseService } from './passphrase.service';

describe('PassphraseService', () => {
  let service: PassphraseService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(PassphraseService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
