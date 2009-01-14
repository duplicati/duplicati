#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Backend
{
    public class SSHBackend : IBackendInterface
    {
        #region IBackendInterface Members

        public string DisplayName
        {
            get { return "SSH based"; }
        }

        public string ProtocolKey
        {
            get { return "ssh"; }
        }

        public List<FileEntry> List(string url, Dictionary<string, string> options)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        public void Put(string url, Dictionary<string, string> options, System.IO.Stream stream)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        public System.IO.Stream Get(string url, Dictionary<string, string> options)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        public void Delete(string url, Dictionary<string, string> options)
        {
            throw new Exception("The method or operation is not implemented.");
        }

        #endregion
    }
}
