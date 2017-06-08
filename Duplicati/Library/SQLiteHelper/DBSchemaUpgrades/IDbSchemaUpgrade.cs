using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.SQLiteHelper.DBUpdates
{

    // TODO: Consider passing in a transaction and performing everything in one go.

    public interface IDbSchemaUpgrade
    {
        /// <summary>
        /// Executed before the textual SQL command is executed.
        /// </summary>
        /// <param name="connection"></param>
        void BeforeSql(System.Data.IDbConnection connection);

        /// <summary>
        /// Executed after the textual SQL command is executed.
        /// </summary>
        /// <param name="connection"></param>
        void AfterSql(System.Data.IDbConnection connection);
    }
}
