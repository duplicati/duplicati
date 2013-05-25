using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Operation
{    
    internal class ListFilesHandler
    {        
        private string m_backendurl;
        private Options m_options;
        private ListResults m_result;

        public ListFilesHandler(string backend, Options options, ListResults result)
        {
            m_backendurl = backend;
            m_options = options;
            m_result = result;
        }

        public void Run(IEnumerable<string> filterstrings = null, Library.Utility.IFilter compositefilter = null)
        {
            var parsedfilter = new Library.Utility.FilterExpression(filterstrings);
            
            var simpleList = !(parsedfilter.Type == Library.Utility.FilterType.Simple || m_options.AllVersions);
			
            Library.Utility.IFilter filter = parsedfilter;
            if (compositefilter != null && !compositefilter.Empty)
                filter = new Library.Utility.CompositeFilterExpression(
                    ((Library.Utility.CompositeFilterExpression)compositefilter).Filters
					.Union(new KeyValuePair<bool, Library.Utility.IFilter>[] { 
						new KeyValuePair<bool, Duplicati.Library.Utility.IFilter>(true, parsedfilter) 
					}),
                    false
                );
        
            //Use a speedy local query
            if (!m_options.NoLocalDb && System.IO.File.Exists(m_options.Dbpath))
                using(var db = new Database.LocalListDatabase(m_options.Dbpath))
                {
                    m_result.SetDatabase(db);
                    using(var filesets = db.SelectFileSets(m_options.Time, m_options.Version))
                    {
                        if (simpleList && parsedfilter.Type != Library.Utility.FilterType.Empty)
                            filesets.TakeFirst();
                            
                        m_result.SetResult(
                            filesets.Times.ToArray(),
                            parsedfilter.Type == Library.Utility.FilterType.Empty ? null :
                                    (from n in filesets.SelectFiles(filter)
                                        select (Duplicati.Library.Interface.IListResultFile)(new ListResultFile(n.Path, n.Sizes.ToArray())))
                                    .ToArray()
                        );

                        return;
                    }
                }
                              
			m_result.AddMessage("No local database, accessing remote store");

            // Otherwise, grab info from remote location
            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(tmpdb, "List"))
            using (var backend = new BackendManager(m_backendurl, m_options, m_result.BackendWriter, db))
            {
                m_result.SetDatabase(db);
                
                var rawlist = backend.List();
                var parsedlist = (from n in rawlist
                            let p = Volumes.VolumeBase.ParseFilename(n)
                            where p != null && p.FileType == RemoteVolumeType.Files
                            orderby p.Time descending
                            select p).ToArray();
                var filelistFilter = RestoreHandler.FilterNumberedFilelist(m_options.Time, m_options.Version);
                var filteredList = filelistFilter(parsedlist).ToList();
                
                if (filteredList.Count == 0)
                    throw new Exception("No filesets found on remote target");
                
                var numberSeq = (from n in filteredList select new KeyValuePair<long, DateTime>(n.Key, n.Value.Time.ToLocalTime())).ToArray();
                
                if (parsedfilter.Type == Library.Utility.FilterType.Empty)
                {
                    m_result.SetResult(numberSeq, null);
                    return;
                }
                
                var firstEntry = filteredList[0].Value;
                filteredList.RemoveAt(0);
                Dictionary<string, List<long>> res; 
                                
                using (var tmpfile = backend.Get(firstEntry.File.Name, firstEntry.File.Size, null))
                using (var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(firstEntry.File.Name), tmpfile, m_options))
                    if (simpleList)
                    {
                        m_result.SetResult(
                            numberSeq.Take(1),
                            (from n in rd.Files
                                  where filter.Matches(n.Path)
                                  orderby n.Path
                                  select new ListResultFile(n.Path, new long[] { n.Size }))
                                  .ToArray()
                        );
                            
                        return;
                    }
                    else
                    {
                        res = rd.Files
                              .Where(x => filter.Matches(x.Path))
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
                    using(var tmpfile = backend.Get(flentry.Value.File.Name, -1, null))
                    using (var rd = new Volumes.FilesetVolumeReader(flentry.Value.CompressionModule, tmpfile, m_options))
                    {
                        foreach(var p in from n in rd.Files where filter.Matches(n.Path) select n)
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

    }
}
