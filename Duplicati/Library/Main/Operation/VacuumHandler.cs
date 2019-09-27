﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal class VacuumHandler
    {
        private readonly Options m_options;
        private readonly VacuumResult m_result;

        public VacuumHandler(Options options, VacuumResult result)
        {
            m_options = options;
            m_result = result;
        }

        public virtual void Run()
        {
            using (var db = new Database.LocalDatabase(m_options.Dbpath, "Vacuum", false))
            {
                m_result.SetDatabase(db);
                db.Vacuum();
            }
        }
    }
}
