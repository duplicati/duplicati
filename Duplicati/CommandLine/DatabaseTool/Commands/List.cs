
// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.Json;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The list command
/// </summary>
public static class List
{
    /// <summary>
    /// Creates the list command
    /// </summary>
    /// <returns>The list command</returns>
    public static Command Create() =>
        new Command("list", "Executes one or more commands on the database")
        {
            new Argument<string>("database", "The database to list"),
            new Argument<string[]>("tables") {
                Arity = ArgumentArity.ZeroOrMore,
                Description = "The table to list. If not specified, all tables will be listed."
            },
            new Option<bool>("--output-json", description: "Output as JSON", getDefaultValue: () => false),
        }
        .WithHandler(CommandHandler.Create<string, string[], bool>((database, tables, outputjson) =>
            {
                if (!File.Exists(database))
                {
                    Console.WriteLine($"Database {database} does not exist");
                    return;
                }

                using var con = SQLiteLoader.LoadConnection(database, 0);
                using var cmd = con.CreateCommand();

                if (tables == null || tables.Length == 0)
                {
                    if (!outputjson)
                        Console.WriteLine("Listing all tables in the database:");

                    var res = new List<string?>();
                    foreach (var rd in cmd.ExecuteReaderEnumerable("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))
                        if (outputjson)
                            res.Add(rd.ConvertValueToString(0));
                        else
                            Console.WriteLine(rd.ConvertValueToString(0) ?? "<null>");

                    if (outputjson)
                        Console.WriteLine(JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true }));
                }
                else
                {
                    foreach (var table in tables)
                    {
                        var res = new List<object?[]>();
                        using var rd = cmd.ExecuteReader($"SELECT * FROM {table}");
                        rd.Print(outputjson);

                        Console.WriteLine();
                    }
                }
            }));
}

