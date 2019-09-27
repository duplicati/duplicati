﻿//  Copyright (C) 2015, The Duplicati Team

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
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation
{
    internal class ListAffected
    {
        private readonly Options m_options;
        private readonly ListAffectedResults m_result;

        public ListAffected(Options options, ListAffectedResults result)
        {
            m_options = options;
            m_result = result;
        }
            
        public void Run(List<string> args, Action<Duplicati.Library.Interface.IListAffectedResults> callback = null)
        {
            if (!System.IO.File.Exists(m_options.Dbpath))
                throw new UserInformationException(string.Format("Database file does not exist: {0}", m_options.Dbpath), "DatabaseDoesNotExist");

            using(var db = new Database.LocalListAffectedDatabase(m_options.Dbpath))
            {
                m_result.SetDatabase(db);
                if (callback == null)
                {
                    m_result.SetResult(
                        db.GetFilesets(args).OrderByDescending(x => x.Time).ToArray(),
                        db.GetFiles(args).ToArray(),
                        db.GetLogLines(args).ToArray(),
                        db.GetVolumes(args).ToArray()
                    );
                }
                else
                {
                    m_result.SetResult(
                        db.GetFilesets(args).OrderByDescending(x => x.Time),
                        db.GetFiles(args),
                        db.GetLogLines(args),
                        db.GetVolumes(args)
                    );

                    callback(m_result);
                }
            }
        }
    }
}

