import { TestBed } from '@angular/core/testing';

import { EditUriService } from './edit-uri.service';

describe('EditUriService', () => {
  let service: EditUriService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(EditUriService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
