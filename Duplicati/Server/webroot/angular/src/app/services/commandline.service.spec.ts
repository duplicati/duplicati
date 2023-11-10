import { TestBed } from '@angular/core/testing';

import { CommandlineService } from './commandline.service';

describe('CommandlineService', () => {
  let service: CommandlineService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(CommandlineService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
