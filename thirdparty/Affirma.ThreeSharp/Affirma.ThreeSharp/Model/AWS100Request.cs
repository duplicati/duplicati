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

namespace Affirma.ThreeSharp.Model
{
    /// <summary>
    /// The base class for all AWS100 Request objects
    /// </summary>
    public class AWS100Request : Request
    {
        private string distributionId;
        private DistributionConfig distributionConfig;

        public AWS100Request()
        {
            this.ServiceType = ThreeSharpServiceType.AWS100;            
        }

        public string DistributionID
        {
            get { return this.distributionId; }
            set { this.distributionId = value; }
        }

        public DistributionConfig DistributionConfig
        {
            get { return this.distributionConfig; }
            set { this.distributionConfig = value; }
        }
    }
}
