using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    internal class TaggedFileEntry : FileEntry
    {
        private readonly Google.Documents.Document m_doc;
        public Google.Documents.Document Doc { get { return m_doc; } }

        public TaggedFileEntry(string name, long size, DateTime accessed, DateTime modified, Google.Documents.Document doc)
            : base(name, size, accessed, modified)
        {
            m_doc = doc;
        }


        public TaggedFileEntry(string name, Google.Documents.Document doc)
            : base(name)
        {
            m_doc = doc;
        }

    }
}
