using System;
using System.Collections.Generic;
using System.Text;

namespace Affirma.ThreeSharp.Model
{
    public class DistributionSummary
    {
        private string id;
        private string status;
        private string lastModifiedTime;
        private string domainName;
        private string origin;
        private string comment;
        private List<string> cnames = new List<string>();
        private bool enabled;

        public DistributionSummary()
        {

        }

        public string Id
        {
            get { return this.id; }
            set { this.id = value; }
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

        public string Origin
        {
            get { return this.origin; }
            set { this.origin = value; }
        }

        public List<string> CNames
        {
            get { return this.cnames; }
            set { this.cnames = value; }
        }

        public string Comment
        {
            get { return this.comment; }
            set { this.comment = value; }
        }

        public bool Enabled
        {
            get { return this.enabled; }
            set { this.enabled = value; }
        }
    }
}
