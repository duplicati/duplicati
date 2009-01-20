using System;
using System.Collections.Generic;
using System.Text;

namespace Affirma.ThreeSharp.Model
{
    public class DistributionInfo
    {
        private string id;
        private string eTagHeader;
        private string status;
        private string lastModifiedTime;
        private string domainName;
        private DistributionConfig config;

        public DistributionInfo()
        {

        }        

        public string Id
        {
            get { return this.id; }
            set { this.id = value; }
        }

        public string ETagHeader
        {
            get { return this.eTagHeader; }
            set { this.eTagHeader = value; }
        }

        public string Status
        {
            get { return this.status; }
            set { this.status = value; }
        }

        public string LastModifiedTime
        {
            get { return this.lastModifiedTime; }
            set { this.lastModifiedTime = value; }
        }

        public string DomainName
        {
            get { return this.domainName; }
            set { this.domainName = value; }
        }

        public DistributionConfig DistributionConfig
        {
            get { return this.config; }
            set { this.config = value; }
        }
    }
}
