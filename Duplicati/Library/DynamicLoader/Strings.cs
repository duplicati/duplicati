using Duplicati.Library.Localization.Short;
namespace Duplicati.Library.DynamicLoader.Strings {
    internal static class DynamicLoader {
        public static string DynamicAssemblyLoadError(string assembly, string message) { return LC.L(@"Failed to load assembly {0}, error message: {1}", assembly, message); }
        public static string DynamicTypeLoadError(string typename, string assembly, string message) { return LC.L(@"Failed to load process type {0} assembly {1}, error message: {2}", typename, assembly, message); }
    }
}
