using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Operation
{
    public interface IListResults
    {
        IEnumerable<KeyValuePair<long, DateTime>> Filesets { get; }
        IEnumerable<IListResultFile> Files { get; }
    }
    
    public interface IListResultFile
    {
        string Path { get; }
        IEnumerable<long> Sizes { get; }
    }
    
    internal class ListFilesHandler : IDisposable
    {        
        private class ListResultFile : IListResultFile
        {
            public string Path { get; private set; }
            public IEnumerable<long> Sizes { get; private set; }
            public ListResultFile(string path, IEnumerable<long> sizes)
            {
                this.Path = path;
                this.Sizes = sizes;
            }
        }
        
        private class ListResults : IListResults
        {
            private IEnumerable<KeyValuePair<long, DateTime>> m_filesets;
            private IEnumerable<IListResultFile> m_files;
            
            public ListResults(IEnumerable<KeyValuePair<long, DateTime>> filesets, IEnumerable<IListResultFile> files)
            {
                m_filesets = filesets;
                m_files = files;
            }
            
            public IEnumerable<KeyValuePair<long, DateTime>> Filesets { get { return m_filesets; } }
            public IEnumerable<IListResultFile> Files { get { return m_files; } }
        }
    
        private string m_backendurl;
        private Options m_options;
        private RestoreStatistics m_stat;

        public ListFilesHandler(string backend, Options options, RestoreStatistics stat)
        {
            m_backendurl = backend;
            m_options = options;
            m_stat = stat;
        }

        public IListResults Run(IEnumerable<string> filter = null)
        {
            var filefilter = new FilterExpression(filter);
            
            var simpleList = !(filefilter.Type == FilterType.Simple || m_options.AllVersions);
        
#if DEBUG
            if (!m_options.NoLocalDb)
#endif
            //Use a speedy local query
            if (System.IO.File.Exists(m_options.Dbpath))
                using (var db = new Database.LocalListDatabase(m_options.Dbpath))
                    using (var filesets = db.SelectFileSets(m_options.Time, m_options.Version))
                    {
                        if (simpleList && filefilter.Type != FilterType.Empty)
                            filesets.TakeFirst();
                        
                        return new ListResults(filesets.Times.ToArray(), 
                            filefilter.Type == FilterType.Empty ? null :
                                (from n in filesets.SelectFiles(filefilter)
                                    select (IListResultFile)(new ListResultFile(n.Path, n.Sizes.ToArray())))
                                .ToArray()
                            );
                    }
                              
			m_stat.LogWarning("No local database, accessing remote store", null);

            // Otherwise, grab info from remote location
            using (var tmpdb = new Library.Utility.TempFile())
            using (var db = new Database.LocalDatabase(tmpdb, "List"))
            using (var backend = new BackendManager(m_backendurl, m_options, m_stat, db))
            {
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
                
                if (filefilter.Type == FilterType.Empty)
                     return new ListResults (numberSeq, null);
                
                var firstEntry = filteredList[0].Value;
                filteredList.RemoveAt(0);
                Dictionary<string, List<long>> res; 
                                
                using (var tmpfile = backend.Get(firstEntry.File.Name, firstEntry.File.Size, null))
                using (var rd = new Volumes.FilesetVolumeReader(RestoreHandler.GetCompressionModule(firstEntry.File.Name), tmpfile, m_options))
                    if (simpleList)
                    {
                        return new ListResults(numberSeq.Take(1), 
                            (from n in rd.Files
                                  where filefilter.Matches(n.Path)
                                  orderby n.Path
                                  select new ListResultFile(n.Path, new long[] { n.Size }))
                                  .ToArray()
                            );
                            
                    }
                    else
                    {
                        res = rd.Files
                              .Where(x => filefilter.Matches(x.Path))
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
                        foreach(var p in from n in rd.Files where filefilter.Matches(n.Path) select n)
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
                
                return new ListResults(
                    numberSeq,
                    from n in res
                    orderby n.Key
                    select (IListResultFile)(new ListResultFile(n.Key, n.Value))
                );                                            
            }

        }

        public void Dispose()
        {
        }
    }
}
