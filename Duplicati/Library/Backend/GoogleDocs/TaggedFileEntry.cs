using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    internal class TaggedFileEntry : FileEntry
    {
        private readonly string m_resourceId;
        private readonly string m_url;
        private readonly string m_mediaUrl;
        private string m_eTag;


        public string ResourceId { get { return m_resourceId; } }
        public string ETag { get { return m_eTag; } set { m_eTag = value; } }
        public string Url { get { return m_url; }  }
        public string MediaUrl { get { return m_mediaUrl; } }

        public TaggedFileEntry(string name, long size, DateTime accessed, DateTime modified, string resourceId, string url, string mediaUrl, string eTag)
            : base(name, size, accessed, modified)
        {
            m_resourceId = resourceId;
            m_mediaUrl = mediaUrl;
            m_url = url;
            m_eTag = eTag;
        }


        public TaggedFileEntry(string name, string resourceId, string eTag, string url, string mediaUrl)
            : base(name)
        {
            m_resourceId = resourceId;
            m_mediaUrl = mediaUrl;
            m_url = url;
            m_eTag = eTag;
        }
        
    }
}
