using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Main.Database
{
    public static class ExtensionMethods
    {
        public static void AddParameters(this System.Data.IDbCommand self, int count)
        {
            for(var i = 0; i < count; i++)
                self.Parameters.Add(self.CreateParameter());
        }

        public static void AddParameter(this System.Data.IDbCommand self)
        {
            self.Parameters.Add(self.CreateParameter());
        }

        public static void AddParameter<T>(this System.Data.IDbCommand self, T value, string name = null)
        {
            var p = self.CreateParameter();
            p.Value = value;
            if (!string.IsNullOrEmpty(name))
                p.ParameterName = name;
            self.Parameters.Add(p);
        }

        public static void SetParameterValue<T>(this System.Data.IDbCommand self, int index, T value)
        {
            ((System.Data.IDataParameter)self.Parameters[index]).Value = value;
        }

        public static int ExecuteNonQuery(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;

            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(new Logging.Timer(LC.L("ExecuteNonQuery: {0}", self.CommandText)))
                return self.ExecuteNonQuery();
        }

        public static object ExecuteScalar(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;
            
            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(new Logging.Timer(LC.L("ExecuteScalar: {0}", self.CommandText)))
                return self.ExecuteScalar();
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, long defaultvalue = -1)
        {
            return ExecuteScalarInt64(self, null, defaultvalue, null);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, string cmd, long defaultvalue = -1)
        {
            return ExecuteScalarInt64(self, cmd, defaultvalue, null);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, string cmd, long defaultvalue, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;

            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(new Logging.Timer(LC.L("ExecuteScalar: {0}", self.CommandText)))
                using(var rd = self.ExecuteReader())
                    if (rd.Read())
                        return ConvertValueToInt64(rd, 0, defaultvalue);

            return defaultvalue;
        }

        public static System.Data.IDataReader ExecuteReader(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;

            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(new Logging.Timer(LC.L("ExecuteReader: {0}", self.CommandText)))
                return self.ExecuteReader();
        }

        public static IEnumerable<System.Data.IDataReader> ExecuteReaderEnumerable(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            using(var rd = ExecuteReader(self, cmd, values))
                while (rd.Read())
                    yield return rd;
        }

        public static string ConvertValueToString(this System.Data.IDataReader reader, int index)
        {
            var v = reader.GetValue(index);
            if (v == null || v == DBNull.Value)
                return null;

            return v.ToString();
        }

        public static long ConvertValueToInt64(this System.Data.IDataReader reader, int index, long defaultvalue = -1)
        {
            try
            {
                if (!reader.IsDBNull(index))
                    return reader.GetInt64(index);
            }
            catch
            {
            }

            return -1;
        }

        public static System.Data.IDbCommand CreateCommand(this System.Data.IDbConnection self, System.Data.IDbTransaction transaction)
        {
            var con = self.CreateCommand();
            con.Transaction = transaction;
            return con;
        }

        /// <summary> Small helper method querying and returning a textual representation of the SQLite execution plan. </summary>
        public static string GetSQLiteExecutionPlan(this System.Data.IDbCommand cmd)
        {
            using (var qpCmd = cmd.Connection.CreateCommand())
            {
                qpCmd.CommandText = "EXPLAIN QUERY PLAN " + cmd.CommandText;
                qpCmd.Connection = cmd.Connection;
                qpCmd.CommandType = cmd.CommandType;
                qpCmd.Transaction = cmd.Transaction;

                foreach (System.Data.IDataParameter p in cmd.Parameters)
                    qpCmd.AddParameter(p.Value, p.ParameterName);

                System.Data.DataTable dt = new System.Data.DataTable();
                using (var rd = qpCmd.ExecuteReader())
                    dt.Load(rd);

                string plan =
                     String.Join("\t", dt.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName).ToArray()) + "\n"
                    + string.Join("\n", dt.Rows.Cast<System.Data.DataRow>().Select(r => String.Join("\t", r.ItemArray.Select(o => o == null ? "" : o.ToString()).ToArray())).ToArray());

                return plan;
            }
        }
        
        public static void DumpSQL(this System.Data.IDbConnection self, System.Data.IDbTransaction trans, string sql, params object[] parameters)
        {
            using (var c = self.CreateCommand())
            {
                c.CommandText = sql;
                c.Transaction = trans;
                if (parameters != null)
                    foreach (var p in parameters)
                        c.AddParameter(p);

                using (var rd = c.ExecuteReader())
                {
                    for (int i = 0; i < rd.FieldCount; i++)
                        Console.Write((i == 0 ? "" : "\t") + rd.GetName(i));
                    Console.WriteLine();

                    long n = 0;
                    while (rd.Read())
                    {
                        for (int i = 0; i < rd.FieldCount; i++)
                            Console.Write(string.Format((i == 0 ? "{0}" : "\t{0}"), rd.GetValue(i)));
                        Console.WriteLine();
                        n++;
                    }
                    Console.WriteLine(LC.L("{0} records", n));
                }
            }
        }

    }
}
