import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class CryptoService {

  constructor() { }

  base64ToBytes(base64: string): Uint8Array {
    const binString = atob(base64);
    return Uint8Array.from(binString, m => m.codePointAt(0)!);
  }
  bytesToBase64(bytes: Uint8Array): string {
    const binString = Array.from(bytes, x => String.fromCodePoint(x)).join('');
    return btoa(binString);
  }

  generateSaltBase64(bytes: number): string {
    const array = new Uint8Array(bytes);
    window.crypto.getRandomValues(array);
    return this.bytesToBase64(array);
  }
  saltedHashBase64(value: string, salt: string): Promise<string> {
    // NOTE: This only works in secure contexts (https or locally served)

    // encode to utf8 bytes
    const valueBytes = new TextEncoder().encode(value);
    // salt is base64
    const saltBytes = this.base64ToBytes(salt);

    const data = new Uint8Array(valueBytes.length + saltBytes.length);
    data.set(valueBytes);
    data.set(saltBytes, valueBytes.length);

    if (!window.crypto.subtle) {
      // Since this is used for generating passwords, and the hash can be intercepted without HTTPS,
      // it is probably better not to give the user an illusion of security
      return Promise.reject(`Can't create hash due to insecure context`);
    }

    return window.crypto.subtle.digest('SHA-256', data).then(v =>
      this.bytesToBase64(new Uint8Array(v))
    );
  }
}
