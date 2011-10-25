using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    internal class TaggedFileEntry : FileEntry
    {
        private readonly string m_cid;
        private readonly string m_url;
        private readonly string m_altUrl;
        private readonly string m_editUrl;

        public string CID { get { return m_cid; } }
        public string Url { get { return m_url; } }

        public string AltUrl { get { return m_altUrl; } }
        public string EditUrl { get { return m_editUrl; } }

        public TaggedFileEntry(string name, long size, DateTime updated, string url, string cid, string altUrl, string editUrl)
            : base(name, size, updated, updated)
        {
            m_cid = cid;
            m_url = url;
            m_altUrl = altUrl;
            m_editUrl = editUrl;
        }


        public TaggedFileEntry(string name, string url, string cid, string altUrl, string editUrl)
            : base(name)
        {
            m_cid = cid;
            m_url = url;
            m_altUrl = altUrl;
            m_editUrl = editUrl;
        }

    }
}
