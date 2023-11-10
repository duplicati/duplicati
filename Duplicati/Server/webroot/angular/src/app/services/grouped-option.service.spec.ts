import { TestBed } from '@angular/core/testing';

import { GroupedOptionService } from './grouped-option.service';

describe('GroupedOptionService', () => {
  let service: GroupedOptionService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(GroupedOptionService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
