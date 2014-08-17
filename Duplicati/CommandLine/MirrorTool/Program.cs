//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using Duplicati.Library.Localization.Short;
using System.Collections.Generic;

namespace Duplicati.CommandLine.MirrorTool
{
    public static class Program
    {
        private class FilterCollector
        {
            private List<Library.Utility.IFilter> m_filters = new List<Library.Utility.IFilter>();
            private Library.Utility.IFilter Filter 
            { 
                get 
                { 
                    if (m_filters.Count == 0)
                        return new Library.Utility.FilterExpression();
                    else if (m_filters.Count == 1)
                        return m_filters[0];
                    else 
                        return m_filters.Aggregate((a,b) => Library.Utility.JoinedFilterExpression.Join(a, b)); 
                }
            }
            
            private Dictionary<string, string> DoExtractOptions(List<string> args, Func<string, string, bool> callbackHandler = null)
            {
                return Library.Utility.CommandLineParser.ExtractOptions(args, (key, value) => {
                    if (key.Equals("include", StringComparison.InvariantCultureIgnoreCase))
                    {
                        m_filters.Add(new Library.Utility.FilterExpression(value, true));
                        return false;
                    }
                    else if (key.Equals("exclude", StringComparison.InvariantCultureIgnoreCase))
                    {
                        m_filters.Add(new Library.Utility.FilterExpression(value, false));
                        return false;
                    }
                    
                    if (callbackHandler != null)
                        return callbackHandler(key, value);
                    
                    return true;
                });
            }
            
            public static Tuple<Dictionary<string, string>, Library.Utility.IFilter> ExtractOptions(List<string> args, Func<string, string, bool> callbackHandler = null)
            {
                var fc = new FilterCollector();
                var opts = fc.DoExtractOptions(args, callbackHandler);
                return new Tuple<Dictionary<string, string>, Library.Utility.IFilter>(opts, fc.Filter);
            }
        }

        private static string GetConflictingRename(string path)
        {
            var p = BackendListSource.SplitPath(path);
            var lix = p.Item2.LastIndexOf('.');
            Tuple<string, string> split;
            if (lix < 0)
                split = new Tuple<string, string>(p.Item2, "");
            else
                split = new Tuple<string, string>(p.Item2.Substring(0, lix), p.Item2.Substring(lix));
            return p.Item1 + '/' + split.Item1 + "-conflicted-" + DateTime.Now.ToString("yyyy-MM-dd-hhmmss") + split.Item2;
        }

        public static int Main(string[] _args)
        {
            var verboseErrors = false;
            try
            {
                var cargs = new List<string>(_args);

                var tmpparsed = FilterCollector.ExtractOptions(cargs);
                var options_dict = tmpparsed.Item1;
                var filter = tmpparsed.Item2;

                var options = new Options(options_dict);
                verboseErrors = options.Verbose || options.DebugOutput;

                if (cargs.Count != 2)
                    throw new Exception(LC.L("You must supply exactly 2 arguments, you supplied {0}: {1}", cargs.Count, Environment.NewLine + string.Join(Environment.NewLine, cargs)));

                var databasepath = options.DbPath;
                if (string.IsNullOrWhiteSpace(options.DbPath) || !System.IO.Path.IsPathRooted(options.DbPath))
                    throw new Exception(LC.L("Invalid or missign database path: {0}", options.DbPath));

                using(var local_probe = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(cargs[0], options.ToDict()))
                using(var remote_probe = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(cargs[1], options.ToDict()))
                {
                    if (local_probe == null)
                        throw new Exception(LC.L("Unable to find a matching backend for \"{0}\"", cargs[0]));
                    if (remote_probe == null)
                        throw new Exception(LC.L("Unable to find a matching backend for \"{0}\"", cargs[1]));


                    if (!(local_probe is Library.Interface.IRenameEnabledBackend))
                    {
                        if (options.ConflictPolicy != Options.ConflictPolicies.ForceLocal && options.ConflictPolicy != Options.ConflictPolicies.KeepLocal)
                            throw new Exception(LC.L("Backend does not support rename: {0}", cargs[0]));
                            
                    }

                    if (!(remote_probe is Library.Interface.IRenameEnabledBackend))
                    {
                        if (options.ConflictPolicy != Options.ConflictPolicies.ForceRemote && options.ConflictPolicy != Options.ConflictPolicies.KeepRemote)
                            throw new Exception(LC.L("Backend does not support rename: {0}", cargs[1]));
                            
                    }
    
                }

                var tempitems = new List<EnumEntry>();

                var newLocals = 0L;
                var newRemotes = 0L;
                var deletedLocals = 0L;
                var deletedRemotes = 0L;
                var changedLocals = 0L;
                var changedRemotes = 0L;

                var startTime = DateTime.Now;
                Console.WriteLine("Running sync ...");

                using(var con = (System.Data.IDbConnection)Activator.CreateInstance(Duplicati.Library.SQLiteHelper.SQLiteLoader.SQLiteConnectionType))
                {
                    con.ConnectionString = "Data Source=" + databasepath;
                    Duplicati.Library.SQLiteHelper.DatabaseUpgrader.UpgradeDatabase(con, databasepath, typeof(Program));

                    using(var local = new BackendListSource(cargs[0], options))
                    using(var remote = new BackendListSource(cargs[1], options))
                    using(var db = new DbListSource(con))
                    using(var merger = new ListSourceMerger(local, remote, db, tempitems))
                        foreach(var e in merger)
                        {
                            if (!Duplicati.Library.Utility.FilterExpression.Matches(filter, e.Path))
                            {
                                if (options.Verbose)
                                    Console.WriteLine(LC.L("Ignoring file due to filter: {0}", e.Path));
                                continue;
                            }
                                
                            var localDeleted = e.LocalTimestamp < 0 && e.LocalRecordedTimestamp >= 0;
                            var remoteDeleted = e.RemoteTimestamp < 0 && e.RemoteRecordedTimestamp >= 0;
                            var localCreated = e.LocalTimestamp >= 0 && e.LocalRecordedTimestamp < 0;
                            var remoteCreated = e.RemoteTimestamp >= 0 && e.RemoteRecordedTimestamp < 0;

                            var localChange = e.LocalTimestamp > e.LocalRecordedTimestamp || localDeleted;
                            var remoteChange = e.RemoteTimestamp > e.RemoteRecordedTimestamp || remoteDeleted;

                            var localChanged = !localDeleted && !localCreated && e.LocalTimestamp > e.LocalRecordedTimestamp;
                            var remoteChanged = !remoteDeleted && !remoteCreated && e.RemoteTimestamp > e.RemoteRecordedTimestamp;

                            // Filter the simple conflict where both are deleted
                            if (localDeleted && remoteDeleted)
                            {
                                local.Delete(e.Path);
                                remote.Delete(e.Path);
                                db.Update(e.Path, -1, -1);
                            }
                            // If a conflict is detected, we end up here
                            else if (localChange && remoteChange)
                            {
                                // Check for remote-forced
                                if (options.SyncDirection == Options.SyncDirections.ToLocal || options.ConflictPolicy == Options.ConflictPolicies.ForceRemote || localDeleted || (!remoteDeleted && options.ConflictPolicy == Options.ConflictPolicies.ForceNewest && e.RemoteTimestamp > e.LocalTimestamp))
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Conflicting change, ignoring local: {0}", e.Path));

                                    tempitems.Add(new EnumEntry() {
                                        Path = e.Path, 
                                        LocalTimestamp = e.LocalRecordedTimestamp,
                                        RemoteTimestamp = e.RemoteTimestamp,
                                        LocalRecordedTimestamp = e.LocalRecordedTimestamp,
                                        RemoteRecordedTimestamp = e.RemoteRecordedTimestamp
                                    });
                                }
                                // Check for local-forced
                                else if (options.SyncDirection == Options.SyncDirections.ToRemote || options.ConflictPolicy == Options.ConflictPolicies.ForceLocal || remoteDeleted || (!localDeleted && options.ConflictPolicy == Options.ConflictPolicies.ForceNewest && e.LocalTimestamp > e.RemoteTimestamp))
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Conflicting change, ignoring remote: {0}", e.Path));

                                    tempitems.Add(new EnumEntry() {
                                        Path = e.Path, 
                                        LocalTimestamp = e.LocalTimestamp,
                                        RemoteTimestamp = e.RemoteRecordedTimestamp,
                                        LocalRecordedTimestamp = e.LocalRecordedTimestamp,
                                        RemoteRecordedTimestamp = e.RemoteRecordedTimestamp
                                    });
                                }
                                // Check for remote-keep
                                else if (options.ConflictPolicy == Options.ConflictPolicies.KeepRemote || (!remoteDeleted && options.ConflictPolicy == Options.ConflictPolicies.KeepNewest && e.RemoteTimestamp > e.LocalTimestamp)) 
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Conflicting change, renaming local: {0}", e.Path));
                                    var newname = GetConflictingRename(e.Path);
                                    var nt = local.Rename(e.Path, newname);

                                    // Insert the resolved entry
                                    tempitems.Add(new EnumEntry() {
                                        Path = e.Path, 
                                        LocalTimestamp = e.LocalRecordedTimestamp,
                                        RemoteTimestamp = e.RemoteTimestamp,
                                        LocalRecordedTimestamp = e.LocalRecordedTimestamp,
                                        RemoteRecordedTimestamp = e.RemoteRecordedTimestamp
                                    });

                                    // Insert the renamed entry
                                    tempitems.Add(new EnumEntry() {
                                        Path = newname, 
                                        LocalTimestamp = nt.Ticks,
                                        RemoteTimestamp = -1,
                                        LocalRecordedTimestamp = nt.Ticks,
                                        RemoteRecordedTimestamp = -1
                                    });
                                }
                                // Check for local-keep
                                else if (options.ConflictPolicy == Options.ConflictPolicies.KeepLocal || (!localDeleted && options.ConflictPolicy == Options.ConflictPolicies.KeepNewest && e.LocalTimestamp > e.RemoteTimestamp)) 
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Conflicting change, renaming remote: {0}", e.Path));
                                    var newname = GetConflictingRename(e.Path);
                                    var nt = remote.Rename(e.Path, newname);

                                    // Insert the resolved entry
                                    tempitems.Add(new EnumEntry() {
                                        Path = e.Path, 
                                        LocalTimestamp = e.LocalTimestamp,
                                        RemoteTimestamp = e.RemoteRecordedTimestamp,
                                        LocalRecordedTimestamp = e.LocalRecordedTimestamp,
                                        RemoteRecordedTimestamp = e.RemoteRecordedTimestamp
                                    });

                                    // Insert the renamed entry
                                    tempitems.Add(new EnumEntry() {
                                        Path = newname, 
                                        LocalTimestamp = -1,
                                        RemoteTimestamp = nt.Ticks,
                                        LocalRecordedTimestamp = -1,
                                        RemoteRecordedTimestamp = nt.Ticks
                                    });
                                }
                                else
                                {
                                    Console.WriteLine(LC.L("Something strange has occurred with the file {0}, please report this to the developers: CONFLICT_SIGNATURE = {1}, {2}, {3}, {4}", e.Path, e.LocalTimestamp, e.LocalRecordedTimestamp, e.RemoteTimestamp, e.RemoteRecordedTimestamp));
                                }
                            }
                            // Simple local change
                            else if (localCreated || localChanged)
                            {
                                if (options.SyncDirection == Options.SyncDirections.ToLocal)
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Local file changed, not uploading local: {0}", e.Path));
                                }
                                else
                                {
                                    if (localCreated)
                                        newLocals++;
                                    else
                                        changedLocals++;

                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Local file changed, uploading local: {0}", e.Path));
                                    
                                    long t;
                                    using(var tmpfile = local.Download(e.Path))
                                        t = remote.Upload(e.Path, tmpfile).Ticks;
                                    db.Update(e.Path, e.LocalTimestamp, t);
                                }
                            }
                            // Simple remote change
                            else if (remoteCreated || remoteChanged)
                            {
                                if (options.SyncDirection == Options.SyncDirections.ToRemote)
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Remote file changed, not downloading to local: {0}", e.Path));
                                }
                                else
                                {
                                    if (localCreated)
                                        newRemotes++;
                                    else
                                        changedRemotes++;

                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Remote file changed, downloading to local: {0}", e.Path));
                                    
                                    long t;
                                    using(var tmpfile = remote.Download(e.Path))
                                        t = local.Upload(e.Path, tmpfile).Ticks;
                                    
                                    db.Update(e.Path, t, e.RemoteTimestamp);
                                }
                            }
                            // Simple local delete
                            else if (localDeleted)
                            {
                                if (options.SyncDirection == Options.SyncDirections.ToRemote)
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Remote file deleted, not deleting local: {0}", e.Path));
                                }
                                else
                                {
                                    deletedLocals++;

                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Local file deleted, deleting remote: {0}", e.Path));
                                    remote.Delete(e.Path);

                                    db.Update(e.Path, e.LocalTimestamp, -1);
                                }
                            }
                            // Simple remote delete
                            else if (remoteDeleted)
                            {
                                if (options.SyncDirection == Options.SyncDirections.ToRemote)
                                {
                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Remote file deleted, not deleting local: {0}", e.Path));
                                }
                                else
                                {
                                    deletedRemotes++;

                                    if (options.Verbose)
                                        Console.WriteLine(LC.L("Remote file deleted, deleting local: {0}", e.Path));
                                    local.Delete(e.Path);

                                    db.Update(e.Path, -1, e.RemoteTimestamp);
                                }
                            }
                            else
                            {
                                if (options.Verbose)
                                    Console.WriteLine(LC.L("File not changed: {0}", e.Path));
                            }
                        }

                        using (var c = con.CreateCommand())
                        {
                            c.CommandText = @"DELETE FROM ""File"" WHERE ""Local"" = ? AND ""Remote"" = ?";
                            var p1 = c.CreateParameter();
                            var p2 = c.CreateParameter();
                            p1.Value = -1;
                            p2.Value = -1;
                            c.Parameters.Add(p1);
                            c.Parameters.Add(p2);

                            c.ExecuteNonQuery();
                        }
                }

                Console.WriteLine("Sync completed in {0}", DateTime.Now - startTime);

                return 0;

            }
            catch (Exception ex)
            {
                while (ex is System.Reflection.TargetInvocationException && ex.InnerException != null)
                    ex = ex.InnerException;

                if (!(ex is Library.Interface.CancelException))
                {
                    if (!string.IsNullOrEmpty(ex.Message))
                    {
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(verboseErrors ? ex.ToString() : ex.Message);
                    }
                }
                else
                {
                    Console.Error.WriteLine(LC.L("A serious error occured: {0}", ex.ToString()));

                    while (ex.InnerException != null)
                    {
                        ex = ex.InnerException;
                        Console.Error.WriteLine();
                        Console.Error.WriteLine(LC.L("  Inner exception: {0}", ex.ToString()));
                    }
                }

                //Error = 100
                return 100;
            }

        }
    }
}
