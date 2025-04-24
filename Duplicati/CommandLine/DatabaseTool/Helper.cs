using System.Data;
using System.Text.Json;

namespace Duplicati.CommandLine.DatabaseTool;

public static class Helper
{
    /// <summary>
    /// Creates a backup of the file
    /// </summary>
    /// <param name="filename">The filename to backup</param>
    public static void CreateFileBackup(string path)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        var filename = Path.GetFileNameWithoutExtension(path);

        var newname = $"{filename}-{DateTime.Now:yyyMMddhhmmss}.bak";
        var backup = Path.Combine(dir, newname);
        var retry = 0;
        while (File.Exists(backup))
        {
            if (retry > 100)
                throw new IOException($"Cannot create backup file {backup} - too many retries");

            retry++;
            newname = $"{filename}-{DateTime.Now:yyyMMddhhmmss}-{retry}.bak";
            backup = Path.Combine(dir, newname);
        }

        File.Copy(path, backup);
    }

    /// <summary>
    /// Prints the data reader to the console
    /// </summary>
    /// <param name="reader">The data reader to print</param>
    /// <param name="useJson">Whether to use JSON or not</param>
    public static void Print(this IDataReader reader, bool useJson)
    {
        if (useJson)
            PrintJson(reader);
        else
            PrintReader(reader);
    }

    /// <summary>
    /// Prints the data reader to the console in JSON format
    /// </summary>
    /// <param name="reader">The data reader to print</param>
    public static void PrintJson(this IDataReader reader)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput(), new JsonWriterOptions { Indented = true });

        writer.WriteStartArray();

        // Write the header row
        writer.WriteStartArray();
        for (int i = 0; i < reader.FieldCount; i++)
            writer.WriteStringValue(reader.GetName(i));
        writer.WriteEndArray();

        // Write each data row
        while (reader.Read())
        {
            writer.WriteStartArray();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                if (value == DBNull.Value)
                    writer.WriteNullValue();
                else if (value is string s)
                    writer.WriteStringValue(s);
                else if (value is int i32)
                    writer.WriteNumberValue(i32);
                else if (value is long i64)
                    writer.WriteNumberValue(i64);
                else if (value is double d)
                    writer.WriteNumberValue(d);
                else if (value is float f)
                    writer.WriteNumberValue(f);
                else if (value is bool b)
                    writer.WriteBooleanValue(b);
                else if (value is DateTime dt)
                    writer.WriteStringValue(dt); // ISO 8601 format
                else
                    writer.WriteStringValue(value.ToString()); // Fallback
            }
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.Flush();
    }

    /// <summary>
    /// Prints the data reader to the console in a tabular format
    /// </summary>
    /// <param name="reader">The data reader to print</param>
    public static void PrintReader(this IDataReader reader)
    {
        // Print the header row
        for (int i = 0; i < reader.FieldCount; i++)
            Console.Write($"{reader.GetName(i)}\t");
        Console.WriteLine();

        // Print each data row
        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                if (value == DBNull.Value)
                    Console.Write("<null>\t");
                else
                    Console.Write($"{value}\t");
            }
            Console.WriteLine();
        }
    }
}