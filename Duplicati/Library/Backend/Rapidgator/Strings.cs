using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend.Strings
{
    internal static class Rapidgator
    {
      public static string Description
      {
        get
        {
          return LC.L("This backend can read and write data to Rapidgator. Allowed format is \"rapidgator://folderid\".");
        }
      }

      public static string DisplayName => LC.L(nameof (Rapidgator));
    }
}
