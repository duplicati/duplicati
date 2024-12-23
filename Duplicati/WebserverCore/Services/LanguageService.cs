using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services
{
    public class LanguageService(IHttpContextAccessor httpContext) : ILanguageService
    {
        private static IEnumerable<string> GetLanguageStrings(HttpRequest? request)
            => request?.GetTypedHeaders()
                .AcceptLanguage?
                .OrderByDescending(x => x.Quality ?? 1)
                .Select(x => x.Value.ToString())
                ?? [];

        private static System.Globalization.CultureInfo? GetLanguage(IEnumerable<string> languages)
            => languages
                .Select(x => Library.Localization.LocalizationService.ParseCulture(x))
                .FirstOrDefault(x => x != null);

        public System.Globalization.CultureInfo? GetLanguage()
            => GetLanguage(GetLanguageStrings(httpContext.HttpContext?.Request));
    }
}