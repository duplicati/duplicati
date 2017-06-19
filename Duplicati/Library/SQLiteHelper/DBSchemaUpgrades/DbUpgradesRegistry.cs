using System.Collections.Generic;
using Duplicati.Library.SQLiteHelper.DBUpdates;

namespace Duplicati.Library.SQLiteHelper.DBSchemaUpgrades
{
    class DbUpgradesRegistry
    {
        /// <summary>
        /// Registry of custom code to be executed along a SQL schema upgrade. The key of the map
        /// represents a 1-based version the upgrade code applies to (after performing the update 
        /// the database will be at that version), the value is an instance of upgrader that 
        /// implements the hooks code.
        /// </summary>
        public static readonly IDictionary<int, IDbSchemaUpgrade> CodeChanges = 
            new Dictionary<int, IDbSchemaUpgrade>()
            {
            };
    }
}
