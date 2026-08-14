
// Copyright (C) 2026, The Duplicati Team
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
using Duplicati.Library.SQLiteHelper;

namespace Duplicati.CommandLine.DatabaseTool.Commands;

/// <summary>
/// The execute command
/// </summary>
public static class Execute
{
    /// <summary>
    /// Creates the execute command
    /// </summary>
    /// <returns>The execute command</returns>
    public static Command Create()
    {
        var databaseArgument = new Argument<string>("database")
        {
            Description = "The databases to downgrade"
        };
        var commandArgument = new Argument<string>("command")
        {
            Description = "The command to run"
        };
        var createBackupOption = new Option<bool>("--create-backup")
        {
            Description = "Create a backup before executing",
            DefaultValueFactory = _ => false
        };
        var outputJsonOption = new Option<bool>("--output-json")
        {
            Description = "Output as JSON",
            DefaultValueFactory = _ => false
        };

        var cmd = new Command("execute", "Executes one or more commands on the database")
        {
            databaseArgument,
            commandArgument,
            createBackupOption,
            outputJsonOption,
        };

        cmd.SetAction(parseResult =>
        {
            var database = parseResult.GetValue(databaseArgument)!;
            var command = parseResult.GetValue(commandArgument)!;
            var createbackup = parseResult.GetValue(createBackupOption);
            var outputjson = parseResult.GetValue(outputJsonOption);

            if (createbackup)
                Helper.CreateFileBackup(database);

            using var connection = SQLiteLoader.LoadConnection(database);
            try
            {
                if (command.IndexOfAny(Path.GetInvalidPathChars()) < 0 && File.Exists(command))
                    command = File.ReadAllText(command);
            }
            catch { }

            var begin = DateTime.Now;

            using var dbcmd = connection.CreateCommand();
            dbcmd.CommandText = command;
            using var rd = dbcmd.ExecuteReader();
            if (!outputjson)
                Console.WriteLine("Execution took: {0:mm\\:ss\\.fff}", DateTime.Now - begin);

            if (rd.FieldCount != 0)
                rd.Print(outputjson);
        });

        return cmd;
    }
}
