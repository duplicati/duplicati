﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{    
    internal class ListFilesHandler
    {        
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<ListFilesHandler>();

        private readonly string m_backendurl;
        private readonly Options m_options;
        private readonly ListResults m_result;

        public ListFilesHandler(string backend, Options options, ListResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }

        public void Run(IEnumerable<string> filterstrings = null, Library.Utility.IFilter compositefilter = null)
        {
            var parsedfilter = new Library.Utility.FilterExpression(filterstrings);
            var filter = Library.Utility.JoinedFilterExpression.Join(parsedfilter, compositefilter);
            var simpleList = !((filter is Library.Utility.FilterExpression && ((Library.Utility.FilterExpression)filter).Type == Library.Utility.FilterType.Simple) || m_options.AllVersions);
        
            //Use a speedy local query
            if (!m_options.NoLocalDb && System.IO.File.Exists(m_options.Dbpath))
                using(var db = new Database.LocalListDatabase(m_options.Dbpath))
                {
                    m_result.SetDatabase(db);
                    using(var filesets = db.SelectFileSets(m_options.Time, m_options.Version))
                    {
                        if (!filter.Empty)
                        {
                            if (simpleList || (m_options.ListFolderContents && !m_options.AllVersions))
                                filesets.TakeFirst();
                        }

                        IEnumerable<Database.LocalListDatabase.IFileversion> files;
                        if (m_options.ListFolderContents)
                            files = filesets.SelectFolderContents(filter);
                        else if (m_options.ListPrefixOnly)
                            files = filesets.GetLargestPrefix(filter);
                        else if (filter.Empty)
                            files = null;
                        else
                            files = filesets.SelectFiles(filter);
                        
                        if (m_options.ListSetsOnly)
                            m_result.SetResult(
                                filesets.QuickSets.Select(x => new ListResultFileset(x.Version, x.Time, x.FileCount, x.FileSizes)).ToArray(),
                                null
                            );
                        else
                            m_result.SetResult(
                                filesets.Sets.Select(x => new ListResultFileset(x.Version, x.Time, x.FileCount, x.FileSizes)).ToArray(),
                                files == null ? null :
                                    (from n in files
                                     select (Duplicati.Library.Interface.IListResultFile)(new ListResultFile(n.Path, n.Sizes.ToArray())))
                                     .ToArray()
                            );
                        

                        return;
                    }
                }
                              
            Logging.Log.WriteInformationMessage(LOGTAG, "NoLocalDatabase", "No local database, accessing remote store");

            //TODO: Add prefix and foldercontents
            if (m_options.ListFolderContents)
                throw new UserInformationException("Listing folder contents is not supported without a local database, consider using the \"repair\" option to rebuild the database.", "FolderContentListingRequiresLocalDatabase");
            else if (m_options.ListPrefixOnly)
                throw new UserInformationException("Listing prefixes is not supported without a local database, consider using the \"repair\" option to rebuild the database.", "PrefixListingRequiresLocalDatabase");

            // Otherwise, grab info from remote location
            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(tmpdb, "List", true))
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);
                
                var filteredList = ParseAndFilterFilesets(backend.List(), m_options);
                if (filteredList.Count == 0)
                    throw new UserInformationException("No filesets found on remote target", "EmptyRemoteFolder");

                var numberSeq = CreateResultSequence(filteredList);
                if (filter.Empty)
                {
                    m_result.SetResult(numberSeq, null);
                    m_result.EncryptedFiles = filteredList.Any(x => !string.IsNullOrWhiteSpace(x.Value.EncryptionModule));
                    return;
                }
                
                var firstEntry = filteredList[0].Value;
                filteredList.RemoveAt(0);
                Dictionary<string, List<long>> res; 
                                
                if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                    return;
                
                using (var tmpfile = backend.Get(firstEntry.File.Name, firstEntry.File.Size, null))
                using (var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(firstEntry.File.Name), tmpfile, m_options))
                    if (simpleList)
                    {
                        m_result.SetResult(
                            numberSeq.Take(1),
                            (from n in rd.Files
                                  where Library.Utility.FilterExpression.Matches(filter, n.Path)
                                  orderby n.Path
                                  select new ListResultFile(n.Path, new long[] { n.Size }))
                                  .ToArray()
                        );
                            
                        return;
                    }
                    else
                    {
                        res = rd.Files
                              .Where(x => Library.Utility.FilterExpression.Matches(filter, x.Path))
                              .ToDictionary(
                                    x => x.Path, 
                                    y => 
                                    { 
                                        var lst = new List<long>();
                                        lst.Add(y.Size);
                                        return lst;
                                    },
                                    Library.Utility.Utility.ClientFilenameStringComparer
                              );
                    }
                    
                long flindex = 1;
                foreach(var flentry in filteredList)
                    using(var tmpfile = backend.Get(flentry.Value.File.Name, flentry.Value.File == null ? -1 : flentry.Value.File.Size, null))
                    using (var rd = new Volumes.FilesetVolumeReader(flentry.Value.CompressionModule, tmpfile, m_options))
                    {
                        if (m_result.TaskControlRendevouz() == TaskControlState.Stop)
                            return;
                        
                        foreach(var p in from n in rd.Files where Library.Utility.FilterExpression.Matches(filter, n.Path) select n)
                        {
                            List<long> lst;
                            if (!res.TryGetValue(p.Path, out lst))
                            {
                                lst = new List<long>();
                                res[p.Path] = lst;
                                for(var i = 0; i < flindex; i++)
                                    lst.Add(-1);
                            }
                            
                            lst.Add(p.Size);
                        }
                        
                        foreach(var n in from i in res where i.Value.Count < flindex + 1 select i)
                            n.Value.Add(-1);
                            
                        flindex++;
                    }
                
                m_result.SetResult(
                    numberSeq,
                    from n in res
                    orderby n.Key
                    select (Duplicati.Library.Interface.IListResultFile)(new ListResultFile(n.Key, n.Value))
               );
            }
        }

        public static List<KeyValuePair<long, Volumes.IParsedVolume>> ParseAndFilterFilesets(IEnumerable<Duplicati.Library.Interface.IFileEntry> rawlist, Options options)
        {
            var parsedlist = (from n in rawlist
                        let p = Volumes.VolumeBase.ParseFilename(n)
                        where p != null && p.FileType == RemoteVolumeType.Files
                        orderby p.Time descending
                        select p).ToArray();
            var filelistFilter = RestoreHandler.FilterNumberedFilelist(options.Time, options.Version);
            return filelistFilter(parsedlist).ToList();                
        }
        
        public static IEnumerable<Library.Interface.IListResultFileset> CreateResultSequence(IEnumerable<KeyValuePair<long, Volumes.IParsedVolume>> filteredList)
        {
            return (from n in filteredList select (Library.Interface.IListResultFileset)(new ListResultFileset(n.Key, n.Value.Time.ToLocalTime(), -1, -1))).ToArray();
        }        
    }
}
