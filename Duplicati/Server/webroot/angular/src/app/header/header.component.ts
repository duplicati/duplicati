import { Component } from '@angular/core';
import { BrandingService } from '../services/branding.service';
import { SystemInfoService } from '../system-info/system-info.service';
import { SystemState } from '../system-info/system-state';

@Component({
  selector: 'app-header',
  templateUrl: './header.component.html',
  styleUrls: ['./header.component.less']
})
export class HeaderComponent {
  constructor(
    public brandingService: BrandingService,
    private systemInfoService: SystemInfoService) { }

  public systemInfo: SystemState | null = null;
  public state: any;
  public throttle_active: boolean = false;

  ngOnInit(): void {
    this.systemInfoService.getState().subscribe(v => { this.systemInfo = v; });
  }

  public pauseOptions(): void {

  }

  public throttleOptions(): void {

  }
}
