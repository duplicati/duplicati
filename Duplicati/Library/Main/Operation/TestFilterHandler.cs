//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
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
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Main.Operation
{
    internal class TestFilterHandler : IDisposable
    {
        private readonly Options m_options;
        private TestFilterResults m_result;
        
        public TestFilterHandler(Options options, TestFilterResults results)
        {
            m_options = options;
            m_result = results;
        }


        public class FilterHandler
        {
            private Snapshots.ISnapshotService m_snapshot;
            private FileAttributes m_attributeFilter;
            private Duplicati.Library.Utility.IFilter m_enumeratefilter;
            private Duplicati.Library.Utility.IFilter m_emitfilter;
            private Options.SymlinkStrategy m_symlinkPolicy;
            private Options.HardlinkStrategy m_hardlinkPolicy;
            private ILogWriter m_logWriter;
            private Dictionary<string, string> m_hardlinkmap;
            private Duplicati.Library.Utility.IFilter m_sourcefilter;
            private Queue<string> m_mixinqueue;

            public FilterHandler(Snapshots.ISnapshotService snapshot, FileAttributes attributeFilter, Duplicati.Library.Utility.IFilter sourcefilter, Duplicati.Library.Utility.IFilter filter, Options.SymlinkStrategy symlinkPolicy, Options.HardlinkStrategy hardlinkPolicy, ILogWriter logWriter)
            {
                m_snapshot = snapshot;
                m_attributeFilter = attributeFilter;
                m_sourcefilter = sourcefilter;
                m_emitfilter = filter;
                m_symlinkPolicy = symlinkPolicy;
                m_hardlinkPolicy = hardlinkPolicy;
                m_logWriter = logWriter;
                m_hardlinkmap = new Dictionary<string, string>();
                m_mixinqueue = new Queue<string>();

                bool includes;
                bool excludes;
                Library.Utility.FilterExpression.AnalyzeFilters(filter, out includes, out excludes);
                if (includes && !excludes)
                {
                    m_enumeratefilter = Library.Utility.FilterExpression.Combine(filter, new Duplicati.Library.Utility.FilterExpression("*" + System.IO.Path.DirectorySeparatorChar, true));
                }
                else
                    m_enumeratefilter = m_emitfilter;

            }

            public bool AttributeFilter(string rootpath, string path, FileAttributes attributes)
            {
                try
                {
                    if (m_snapshot.IsBlockDevice(path))
                    {
                        if (m_logWriter != null)
                            m_logWriter.AddVerboseMessage("Excluding block device: {0}", path);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddWarning(string.Format("Failed to process path: {0}", path), ex);
                    return false;
                }

                Duplicati.Library.Utility.IFilter sourcematch;
                bool sourcematches;
                if (m_sourcefilter.Matches(path, out sourcematches, out sourcematch) && sourcematches)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Including source path: {0}", path);

                    return true;
                }

                if (m_hardlinkPolicy != Options.HardlinkStrategy.All)
                {
                    try
                    {
                        var id = m_snapshot.HardlinkTargetID(path);
                        if (id != null)
                        {
                            if (m_hardlinkPolicy == Options.HardlinkStrategy.None)
                            {
                                if (m_logWriter != null)
                                    m_logWriter.AddVerboseMessage("Excluding hardlink: {0} ({1})", path, id);
                                return false;
                            }
                            else if (m_hardlinkPolicy == Options.HardlinkStrategy.First)
                            {
                                string prevPath;
                                if (m_hardlinkmap.TryGetValue(id, out prevPath))
                                {
                                    if (m_logWriter != null)
                                        m_logWriter.AddVerboseMessage("Excluding hardlink ({1}) for: {0}, previous hardlink: {2}", path, id, prevPath);
                                    return false;
                                }
                                else
                                {
                                    m_hardlinkmap.Add(id, path);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (m_logWriter != null)
                            m_logWriter.AddWarning(string.Format("Failed to process path: {0}", path), ex);
                        return false;
                    }                    
                }

                if ((m_attributeFilter & attributes) != 0)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Excluding path due to attribute filter: {0}", path);
                    return false;
                }

                Library.Utility.IFilter match;
                if (!Library.Utility.FilterExpression.Matches(m_enumeratefilter, path, out match))
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Excluding path due to filter: {0} => {1}", path, match == null ? "null" : match.ToString());
                    return false;
                }
                else if (match != null)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Including path due to filter: {0} => {1}", path, match.ToString());
                }

                var isSymlink = (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                if (isSymlink && m_symlinkPolicy == Options.SymlinkStrategy.Ignore)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Excluding symlink: {0}", path);
                    return false;
                }

                if (isSymlink && m_symlinkPolicy == Options.SymlinkStrategy.Store)
                {
                    if (m_logWriter != null)
                        m_logWriter.AddVerboseMessage("Storing symlink: {0}", path);

                    m_mixinqueue.Enqueue(path);
                    return false;
                }

                return true;
            }

            public IEnumerable<string> EnumerateFilesAndFolders()
            {
                foreach(var s in m_snapshot.EnumerateFilesAndFolders(this.AttributeFilter, (root, path, ex) => { }))
                {
                    while (m_mixinqueue.Count > 0)
                        yield return m_mixinqueue.Dequeue();

                    Library.Utility.IFilter m;
                    if (m_emitfilter != m_enumeratefilter && !Library.Utility.FilterExpression.Matches(m_emitfilter, s, out m))
                        continue;

                    yield return s;
                }

                while (m_mixinqueue.Count > 0)
                    yield return m_mixinqueue.Dequeue();
            }

            public IEnumerable<string> Mixin(IEnumerable<string> list)
            {
                foreach(var s in list.Where(x => {
                    var fa = FileAttributes.Normal;
                    try { fa = m_snapshot.GetAttributes(x); }
                    catch { }

                    return AttributeFilter(null, x, fa);
                }))
                {
                    while (m_mixinqueue.Count > 0)
                        yield return m_mixinqueue.Dequeue();

                    yield return s;
                }

                while (m_mixinqueue.Count > 0)
                    yield return m_mixinqueue.Dequeue();
            }
        }


        
        public void Run(string[] sources, Library.Utility.IFilter filter)
        {
            var storeSymlinks = m_options.SymlinkPolicy == Duplicati.Library.Main.Options.SymlinkStrategy.Store;
            var sourcefilter = new Library.Utility.FilterExpression(sources, true);

            using(var snapshot = BackupHandler.GetSnapshot(sources, m_options, m_result))
            {
                foreach(var path in new FilterHandler(snapshot, m_options.FileAttributeFilter, sourcefilter, filter, m_options.SymlinkPolicy, m_options.HardlinkPolicy, m_result).EnumerateFilesAndFolders())
                {
                    var fa = FileAttributes.Normal;
                    try { fa = snapshot.GetAttributes(path); }
                    catch { }
                    
                    if (storeSymlinks && ((fa & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint))
                    {
                        m_result.AddVerboseMessage("Storing symlink: {0}", path);
                    }
                    else if ((fa & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        m_result.AddVerboseMessage("Including folder: {0}", path);
                    }
                    else
                    {
                        m_result.FileCount++;
                        var size = -1L;
                    
                        try
                        {
                            size = snapshot.GetFileSize(path);
                            m_result.FileSize += size;
                        }
                        catch
                        {
                        }
                    
                    
                        if (m_options.SkipFilesLargerThan == long.MaxValue || m_options.SkipFilesLargerThan == 0 || size < m_options.SkipFilesLargerThan)
                            m_result.AddVerboseMessage("Including file: {0} ({1})", path, size <= 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));
                        else
                            m_result.AddVerboseMessage("Excluding file due to size: {0} ({1})", path, size <= 0 ? "unknown" : Duplicati.Library.Utility.Utility.FormatSizeString(size));                    
                    }
                    
                }
            }
        }
        
        #region IDisposable implementation
        public void Dispose()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}

