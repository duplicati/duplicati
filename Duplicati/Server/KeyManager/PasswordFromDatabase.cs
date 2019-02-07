//  Copyright (C) 2019, The Duplicati Team
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
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using System.Linq;


namespace Duplicati.Security.KeyManager
{
    public class PasswordFromDatabase : IPasswordService
    {

        private readonly Connection con;

        public PasswordFromDatabase(Connection con)
        {
            this.con = con;
        }

        public string GetPassphrase(long backupId)
        {
            ISetting[] settings = con.GetSettings(backupId);
            return settings.Where(s => s.Name == "passphrase")
                           .Select(s => s.Value).FirstOrDefault();
        }

        public string PasswordSalt => throw new NotImplementedException();
    }
}
