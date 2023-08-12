import { Injectable } from '@angular/core';

@Injectable({
  providedIn: 'root'
})
export class PassphraseService {

  constructor() { }

  computeStrength(passphrase: string): number {
    //return (zxcvbn(passphrase.substring(0, 100)) || { 'score': -1 }).score;
    return 0;
  }

  generatePassphrase(): string {
    const specials = '!@#$%^&*()_+{}:"<>?[];\',./';
    const lowercase = 'abcdefghijklmnopqrstuvwxyz';
    const uppercase = lowercase.toUpperCase();
    const numbers = '0123456789';
    const all = specials + lowercase + uppercase + numbers;

    // TODO: Don't use Math.random() for secure random numbers, use crypto.getRandomValues()

    function choose(str: string, n: number) {
      var res = '';
      for (var i = 0; i < n; i++) {
        res += str.charAt(Math.floor(Math.random() * str.length));
      }

      return res;
    };

    var pwd = (
      choose(specials, 2) +
      choose(lowercase, 2) +
      choose(uppercase, 2) +
      choose(numbers, 2) +
      choose(all, (Math.random() * 5) + 5)
    ).split('');

    for (var i = 0; i < pwd.length; i++) {
      var pos = Math.floor(Math.random() * pwd.length);
      var t = pwd[i]
      pwd[i] = pwd[pos];
      pwd[pos] = t;
    }

    return pwd.join('');
  }
}
