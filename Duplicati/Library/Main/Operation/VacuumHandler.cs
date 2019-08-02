using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Main.Operation
{
    internal class VacuumHandler
    {
        private readonly Options m_options;
        private readonly VacuumResults m_result;

        public VacuumHandler(Options options, VacuumResults result)
        {
            m_options = options;
            m_result = result;
        }

        public virtual void Run()
        {
            using (var db = new Database.LocalDatabase(m_options.Dbpath, "Vacuum", false))
            {
                m_result.SetDatabase(db);
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Vacuum_Running);
                db.Vacuum();
                m_result.EndTime = DateTime.UtcNow;
            }
        }
    }
}
