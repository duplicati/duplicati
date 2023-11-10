import { NgZone } from '@angular/core';
import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { DialogService } from '../services/dialog.service';
import { ImportService } from '../services/import.service';
import { UrlService } from '../services/url.service';

@Component({
  selector: 'app-import',
  templateUrl: './import.component.html',
  styleUrls: ['./import.component.less']
})
export class ImportComponent {
  connecting: boolean = false;
  completed: boolean = false;
  importURL?: string;
  restoremode: boolean = false;

  passphrase: string = '';
  importMetadata: boolean = true;
  callbackMethod: string = '';

  constructor(private router: Router,
    private route: ActivatedRoute,
    private dialog: DialogService,
    private importService: ImportService,
    private ngZone: NgZone,
    private urlService: UrlService) { }

  ngOnInit() {
    this.importURL = this.urlService.getImportUrl();
    // Ugly, but we need to communicate across the iframe load
    this.callbackMethod = 'callback-' + Math.random();
    (window as any)[this.callbackMethod] = (message: string, jobdefinition: any) => {
      // This is called from outside
      this.ngZone.run(() =>
        // The delay fixes an issue with Ghostery failing somewhere
        setTimeout(() => {
          this.connecting = false;
          this.completed = true;

          if (message === 'OK') {
            this.router.navigate(['/']);
          } else if (jobdefinition != null && typeof (jobdefinition) !== 'string') {

            this.importService.setImportData(jobdefinition);
            if (this.restoremode) {
              this.router.navigate(['/restoredirect-import']);
            } else {
              this.router.navigate(['/add-import']);
            }
          } else {
            this.dialog.dialog($localize`Error`, message);
          }
        }, 100));
    };
    this.restoremode = this.route.snapshot.data['restoremode'] ?? false;
    this.importService.resetImportData();
  }

  ngOnDestroy() {
    (window as any)[this.callbackMethod] = undefined;
  }
  doSubmit() {
    this.connecting = true;
  }
}
