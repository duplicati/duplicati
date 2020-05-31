using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Main.Database
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(ExtensionMethods));

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

        public static string GetPrintableCommandText(this System.Data.IDbCommand self)
        {
            var txt = self.CommandText;

            foreach(var p in self.Parameters.Cast<System.Data.IDbDataParameter>())
            {
                var ix = txt.IndexOf('?');
                if (ix >= 0)
                {
                    string v;
                    if (p.Value is string)
                        v = string.Format("\"{0}\"", p.Value);
                    else if (p.Value == null)
                        v = "NULL";
                    else
                        v = string.Format("{0}", p.Value);

                    txt = txt.Substring(0, ix) + v + txt.Substring(ix + 1);
                }
            }

            return txt;
        }

        public static int ExecuteNonQuery(this System.Data.IDbCommand self, bool writeLog)
        {
            return ExecuteNonQuery(self, writeLog, null, null);
        }

        public static int ExecuteNonQuery(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            return ExecuteNonQuery(self, true, cmd, values);
        }

        public static int ExecuteNonQuery(this System.Data.IDbCommand self, bool writeLog, string cmd, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;

            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(writeLog ? new Logging.Timer(LOGTAG, "ExecuteNonQuery", string.Format("ExecuteNonQuery: {0}", self.GetPrintableCommandText())) : null)
                return self.ExecuteNonQuery();
        }

        public static object ExecuteScalar(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            return ExecuteScalar(self, true, cmd, values);
        }

        public static object ExecuteScalar(this System.Data.IDbCommand self, bool writeLog, string cmd, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;
            
            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(writeLog ? new Logging.Timer(LOGTAG, "ExecuteScalar", string.Format("ExecuteScalar: {0}", self.GetPrintableCommandText())) : null)
                return self.ExecuteScalar();
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, bool writeLog, long defaultvalue = -1)
        {
            return ExecuteScalarInt64(self, writeLog, null, defaultvalue);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, long defaultvalue = -1)
        {
            return ExecuteScalarInt64(self, true, null, defaultvalue);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, bool writeLog, string cmd, long defaultvalue = -1)
        {
            return ExecuteScalarInt64(self, writeLog, cmd, defaultvalue, null);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, string cmd, long defaultvalue = -1)
        {
            return ExecuteScalarInt64(self, true, cmd, defaultvalue, null);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, string cmd, long defaultvalue, params object[] values)
        {
            return ExecuteScalarInt64(self, true, cmd, defaultvalue, values);
        }

        public static long ExecuteScalarInt64(this System.Data.IDbCommand self, bool writeLog, string cmd, long defaultvalue, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;

            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(writeLog ? new Logging.Timer(LOGTAG, "ExecuteScalarInt64", string.Format("ExecuteScalarInt64: {0}", self.GetPrintableCommandText())) : null)
                using(var rd = self.ExecuteReader())
                    if (rd.Read())
                        return ConvertValueToInt64(rd, 0, defaultvalue);

            return defaultvalue;
        }

        public static System.Data.IDataReader ExecuteReader(this System.Data.IDbCommand self, string cmd, params object[] values)
        {
            return ExecuteReader(self, true, cmd, values);
        }

        public static System.Data.IDataReader ExecuteReader(this System.Data.IDbCommand self, bool writeLog, string cmd, params object[] values)
        {
            if (cmd != null)
                self.CommandText = cmd;

            if (values != null && values.Length > 0)
            {
                self.Parameters.Clear();
                foreach (var n in values)
                    self.AddParameter(n);
            }

            using(writeLog ? new Logging.Timer(LOGTAG, "ExecuteReader", string.Format("ExecuteReader: {0}", self.GetPrintableCommandText())) : null)
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

            return defaultvalue;
        }

        public static System.Data.IDbCommand CreateCommand(this System.Data.IDbConnection self, System.Data.IDbTransaction transaction)
        {
            var con = self.CreateCommand();
            con.Transaction = transaction;
            return con;
        }
    }
}
