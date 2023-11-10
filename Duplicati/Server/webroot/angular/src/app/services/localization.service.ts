import { HttpClient, HttpContext } from '@angular/common/http';
import { Inject, Injectable, LOCALE_ID } from '@angular/core';
import { loadTranslations } from '@angular/localize';
import { EMPTY } from 'rxjs';
import { map, Observable } from 'rxjs';
import { MESSAGES } from '../../locales/messages-all';
import { ADD_API_URL } from '../interceptors/api-url-interceptor';

@Injectable({
  providedIn: 'root'
})
export class LocalizationService {

  readonly defaultLocale = 'en';

  constructor(@Inject(LOCALE_ID) private locale: string,
    private client: HttpClient) { }

  private localeToDisplay(): string {
    if (this.locale in MESSAGES) {
      return this.locale;
    }
    let normalized = this.locale.replace('_', '-');
    let idx = normalized.indexOf('-');
    normalized = normalized.substring(0, idx).toLowerCase() + '-' + normalized.substring(idx).toUpperCase();
    if (normalized in MESSAGES) {
      return normalized;
    }
    let langOnly = normalized.substring(0, idx);
    if (langOnly in MESSAGES) {
      return langOnly;
    }
    return this.defaultLocale;
  }

  loadLanguage() { return this.loadLanguageImport(); }

  loadLanguageImport(): Observable<void> {
    // Load translation bundled with app
    // Advantages:
    // - loads faster
    // Disadvantages:
    // - have to load all languages at once
    // - json files need to be converted to scripts
    const locale = this.localeToDisplay();
    const translations = MESSAGES[locale];
    if (translations != null) {
      loadTranslations(translations);
    }
    return EMPTY;
  }
  loadLanguageHttp(): Observable<void> {
    // Load translation with extra request
    if (this.locale == this.defaultLocale) {
      return EMPTY;
    }
    return this.client.get<{ locale: string, translations: Record<string, string> }>(`assets/messages.${this.locale}.json`,
      {
        context: new HttpContext().set(ADD_API_URL, false)
      }).pipe(
        map(d => { loadTranslations(d.translations); })
      );
  }
}
