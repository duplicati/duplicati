namespace Duplicati.Library.Utility
{
    /// <summary>
    /// EnumerationFilter result.
    /// </summary>
    public class EnumerationFilterResult
    {
        public EnumerationFilterResult(bool shouldRecurse)
        {
            ShouldRecurse = shouldRecurse;
            IsSourceFilterMatch = false;
        }

        public EnumerationFilterResult(bool shouldRecurse, bool isSourceFilterMatch)
        {
            ShouldRecurse = shouldRecurse;
            IsSourceFilterMatch = isSourceFilterMatch;
        }

        /// <summary>
        /// For folders, true if folder should be recursed.
        /// </summary>
        public bool ShouldRecurse { get; private set; }
        /// <summary>
        /// True if path matched because of a direct source filter match.
        /// </summary>
        public bool IsSourceFilterMatch { get; private set; }

        /// <summary>
        /// EnumerationFilterResult representing a true result with
        /// IsSourceFilter set to false.
        /// </summary>
        public static EnumerationFilterResult True = new EnumerationFilterResult(true);

        /// <summary>
        /// EnumerationFilterResult representing a false result.
        /// </summary>
        public static EnumerationFilterResult False = new EnumerationFilterResult(false);
    }
}
