import { StaticProvider } from "@angular/core";
import { inject } from "@angular/core";
import { of } from "rxjs";
import { CommonBackendData } from "../../backend-editor";
import { EditUriService } from "../../services/edit-uri.service";
import { GENERIC_VALIDATORS } from "./generic.component";

function ftpValidator() {
  const editUri = inject(EditUriService);
  let fun = (data: CommonBackendData) => {
    const res = editUri.requireServer(data)
      && editUri.requireField(data, 'username', $localize`Username`);

    if (res) {
      return editUri.recommendPath(data);
    }
    return of(false);
  };
  return { key: 'ftp', value: fun };
}
function aftpValidator() {
  return { key: 'aftp', value: ftpValidator().value };
}
function sshValidator() {
  const editUri = inject(EditUriService);
  let fun = (data: CommonBackendData) => {
    const res = editUri.requireServer(data)
      && editUri.requireField(data, 'username', $localize`Username`);

    if (res) {
      return editUri.recommendPath(data);
    }
    return of(false);
  };
  return { key: 'ssh', value: fun };
}
function webdavValidator() {
  return { key: 'webdav', value: sshValidator().value };
}
function cloudfilesValidator() {
  return { key: 'cloudfiles', value: sshValidator().value };
}
function tahoeValidator() {
  return { key: 'tahoe', value: sshValidator().value };
}

export const genericValidatorProviders: StaticProvider[] = [
  { provide: GENERIC_VALIDATORS, useFactory: ftpValidator, multi: true },
  { provide: GENERIC_VALIDATORS, useFactory: aftpValidator, multi: true },
  { provide: GENERIC_VALIDATORS, useFactory: sshValidator, multi: true },
  { provide: GENERIC_VALIDATORS, useFactory: webdavValidator, multi: true },
  { provide: GENERIC_VALIDATORS, useFactory: cloudfilesValidator, multi: true },
  { provide: GENERIC_VALIDATORS, useFactory: tahoeValidator, multi: true },
];
