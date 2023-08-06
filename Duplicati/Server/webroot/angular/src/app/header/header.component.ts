import { Component, EventEmitter, Output } from '@angular/core';
import { BrandingService } from '../services/branding.service';
import { SystemInfoService } from '../system-info/system-info.service';
import { SystemInfo } from '../system-info/system-info';
import { Observable } from 'rxjs';
import { ServerStatus } from '../services/server-status';

@Component({
  selector: 'app-header',
  templateUrl: './header.component.html',
  styleUrls: ['./header.component.less']
})
export class HeaderComponent {
  constructor(
    public brandingService: BrandingService,
    private systemInfoService: SystemInfoService) { }

  public systemInfo?: SystemInfo;
  public state?: ServerStatus;
  public throttle_active: boolean = false;

  @Output() toggleMenu = new EventEmitter<void>();


  ngOnInit(): void {
    this.systemInfoService.getState().subscribe(v => { this.systemInfo = v; });
  }

  public pauseOptions(): void {

  }

  public throttleOptions(): void {

  }

  onClickMenu(event: Event) {
    event.stopPropagation();
    event.preventDefault();
    this.toggleMenu.emit();
  }
}
