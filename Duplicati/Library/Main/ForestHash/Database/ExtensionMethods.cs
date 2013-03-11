using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
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

        public static void AddParameter<T>(this System.Data.IDbCommand self, T value)
        {
            var p = self.CreateParameter();
            p.Value = value;
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

            return self.ExecuteScalar();
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

            return self.ExecuteReader();
        }

        public static void DumpSQL(this System.Data.IDbConnection self, string sql, params object[] parameters)
        {
            using (var c = self.CreateCommand())
            {
                c.CommandText = sql;
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
                    Console.WriteLine("{0} records", n);
                }
            }
        }

    }
}
