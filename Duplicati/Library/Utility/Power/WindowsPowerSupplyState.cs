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

using System.Windows.Forms;

namespace Duplicati.Library.Utility.Power
{
    public class WindowsPowerSupplyState : IPowerSupplyState
    {
        public PowerSupply.Source GetSource()
        {
            try
            {
                PowerLineStatus status = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
                if (status == PowerLineStatus.Online)
                {
                    return PowerSupply.Source.AC;
                }
                if (status == PowerLineStatus.Offline)
                {
                    return PowerSupply.Source.Battery;
                }

                return PowerSupply.Source.Unknown;
            }
            catch
            {
                return PowerSupply.Source.Unknown;
            }
        }
    }
}
