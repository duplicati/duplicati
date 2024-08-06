namespace Duplicati.WebserverCore.Abstractions;

public interface ILanguageService
{
    System.Globalization.CultureInfo? GetLanguage();
}
