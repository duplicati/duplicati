import { Injectable } from '@angular/core';

// Based on https://github.com/angular/material.angular.io/blob/master/src/app/shared/style-manager/style-manager.ts
@Injectable({
  providedIn: 'root'
})
export class StyleManagerService {

  constructor() { }

  setStyle(key: string, href: string) {
    getLinkElementForKey(key).setAttribute("href", href);
  }

  removeStyle(key: string) {
    const existingLinkElement = getExistingLinkElementByKey(key);
    if (existingLinkElement) {
      document.head.removeChild(existingLinkElement);
    }
  }
}

function getLinkElementForKey(key: string): HTMLLinkElement {
  return getExistingLinkElementByKey(key) || createLinkElementWithKey(key);
}

function getExistingLinkElementByKey(key: string): HTMLLinkElement | null {
  return document.head.querySelector(`link[rel="stylesheet"].app-${key}`);
}

function createLinkElementWithKey(key: string): HTMLLinkElement {
  const e = document.createElement("link");
  e.setAttribute("rel", "stylesheet");
  e.classList.add(`app-${key}`);
  document.head.appendChild(e);
  return e;
}
