/******************************************************************************* 
 *  Licensed under the Apache License, Version 2.0 (the "License"); 
 *  
 *  You may not use this file except in compliance with the License. 
 *  You may obtain a copy of the License at: http://www.apache.org/licenses/LICENSE-2.0.html 
 *  This file is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
 *  CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 *  specific language governing permissions and limitations under the License.
 * ***************************************************************************** 
 * 
 *  Joel Wetzel
 *  Affirma Consulting
 *  jwetzel@affirmaconsulting.com
 * 
 */

using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using Affirma.ThreeSharp.Model;

namespace Affirma.ThreeSharp.Statistics
{
    /// <summary>
    /// Keeps track of all transfers made by the ThreeSharpQuery class.
    /// </summary>
    public class ThreeSharpStatistics
    {
        private Hashtable transfers;

        public ThreeSharpStatistics()
        {
            this.transfers = new Hashtable();
        }

        public void AddTransfer(Transfer transfer)
        {
            this.transfers.Add(transfer.ID, transfer);
        }

        public void RemoveTransfer(String id)
        {
            if (!this.transfers.ContainsKey(id))
            {
                throw new ArgumentOutOfRangeException("Transfer.ID", "The Transfer.ID does not exist to be removed.");
            }

            this.transfers.Remove(id);
        }

        public Transfer[] GetTransfers()
        {
            List<Transfer> trfrs = new List<Transfer>();
            foreach (Transfer trfr in this.transfers.Values)
            {
                trfrs.Add(trfr);
            }
            return trfrs.ToArray();
        }

        public Transfer GetTransfer(String id)
        {
            return (Transfer)this.transfers[id];
        }

        public long TotalBytesUploaded
        {
            get
            {
                long count = 0;
                foreach (Transfer transfer in this.transfers.Values)
                {
                    if (transfer.Method == "PUT")
                    {
                        count += transfer.BytesTransferred;
                    }
                }
                return count;
            }
        }

        public long TotalBytesDownloaded
        {
            get
            {
                long count = 0;
                foreach (Transfer transfer in this.transfers.Values)
                {
                    if (transfer.Method == "GET")
                    {
                        count += transfer.BytesTransferred;
                    }
                }
                return count;
            }
        }

    }
}
