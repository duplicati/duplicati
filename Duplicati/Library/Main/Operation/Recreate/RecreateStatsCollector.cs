//  Copyright (C) 2017, The Duplicati Team
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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main.Operation.Common;

namespace Duplicati.Library.Main.Operation.Recreate
{
    internal class RecreateStatsCollector : StatsCollector
    {
        private readonly RecreateDatabaseResults m_res;

        public RecreateStatsCollector(RecreateDatabaseResults res)
            : base(res.BackendWriter)
        {
            m_res = res;
        }

        public void UpdatePhase(OperationPhase phase)
        {
            m_res.OperationProgressUpdater.UpdatePhase(phase);
        }

        public void UpdateProgress(float pg)
        {
            m_res.OperationProgressUpdater.UpdateProgress(pg);
        }

        public Task SetEndTimeAsync()
        {
            return RunOnMain(() =>
            {
                m_res.EndTime = DateTime.UtcNow;
            });
        }
    }
}
