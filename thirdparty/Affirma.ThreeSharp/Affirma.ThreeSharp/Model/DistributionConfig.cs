using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;

namespace Affirma.ThreeSharp.Model
{
    public class DistributionConfig
    {
        string origin;
        string callerReference;
        List<string> cnames = new List<string>();
        string comment;
        bool enabled = false;

        public DistributionConfig()
        {

        }       

        public XmlDocument ToXml()
        {
            string xmlStr = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><DistributionConfig xmlns=\"http://cloudfront.amazonaws.com/doc/2008-06-30/\">";

            if (!string.IsNullOrEmpty(this.origin)) xmlStr += "<Origin>" + ((this.origin.Contains(".s3.amazonaws.com")) ? this.origin : this.origin + ".s3.amazonaws.com") + "</Origin>";
            //else xmlStr += "<Origin/>";

            if (!string.IsNullOrEmpty(this.callerReference)) xmlStr += "<CallerReference>" + this.callerReference + "</CallerReference>";
            //else xmlStr += "<CallerReference/>";
            
            if (this.cnames.Count > 0)
            {
                foreach (string cname in this.cnames)
                {
                    if (!string.IsNullOrEmpty(cname)) 
                        xmlStr += "<CNAME>" + cname + "</CNAME>";
                }
            }

            if (!string.IsNullOrEmpty(this.comment)) xmlStr += "<Comment>" + this.comment + "</Comment>";
            //else xmlStr += "<Comment/>";

            xmlStr += "<Enabled>" + ((this.enabled) ? "true" : "false") +"</Enabled>";

            xmlStr += "</DistributionConfig>";

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlStr);

            return xmlDoc;
        }

        public string Origin
        {
            get { return this.origin; }
            set { this.origin = value; }
        }

        public string CallerReference
        {
            get { return this.callerReference; }
            set { this.callerReference = value; }
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
