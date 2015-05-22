using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend.GoogleCloudstore
{
//    public class GS : IBackend, IStreamingBackend, IRenameEnabledBackend
    public class GS : IBackend, IStreamingBackend
    {

        private string m_project;
        private string m_bucket;
        private string m_prefix;
        private GSWrapper m_wrapper;

        private Dictionary<string, string> m_options;

        public GS()
        {

        }

        public GS(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            this.m_bucket = uri.Host;
            this.m_prefix = uri.Path;
            if (!this.m_prefix.EndsWith("/"))
                this.m_prefix = this.m_prefix + "/";

            string gsID = null;
            string gsKey = null;

            if (options.ContainsKey("auth-username"))
                gsID = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                gsKey = options["auth-password"];

            this.m_project = null;
            if (options.ContainsKey("gs-project"))
                this.m_project = options["gs-project"];
            //this.m_bucket = null;
            //if (options.ContainsKey("gs-bucket"))
            //    this.m_bucket = options["gs-bucket"];

            this.m_wrapper = new GSWrapper(this.m_project, gsID, gsKey);
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return "Google Cloud Storage"; }
        }

        public string ProtocolKey
        {
            get { return "gs"; }
        }

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public List<IFileEntry> List()
        {
            try
            {
                List<IFileEntry> lst = Connection.ListBucket(m_bucket, m_prefix);
                for (int i = 0; i < lst.Count; i++)
                {
                    ((FileEntry)lst[i]).Name = lst[i].Name.Substring(m_prefix.Length);

                    //Fix for a bug in Duplicati 1.0 beta 3 and earlier, where filenames are incorrectly prefixed with a slash
                    if (lst[i].Name.StartsWith("/") && !m_prefix.StartsWith("/"))
                        ((FileEntry)lst[i]).Name = lst[i].Name.Substring(1);
                }
                return lst;
            }
            catch (Exception ex)
            {
                //Catch "non-existing" buckets
/*                Amazon.S3.AmazonS3Exception s3ex = ex as Amazon.S3.AmazonS3Exception;
                if (s3ex != null && (s3ex.StatusCode == System.Net.HttpStatusCode.NotFound || "NoSuchBucket".Equals(s3ex.ErrorCode)))
                    throw new Interface.FolderMissingException(ex); */

                throw new Interface.FolderMissingException(ex);
            }
        }

        public void Put(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, fs);
        }

        public void Put(string remotename, System.IO.Stream input)
        {
            try
            {
                Connection.AddFileStream(m_bucket, this.m_prefix + remotename, input);
            }
            catch (Exception ex)
            {
                throw;
                //throw new Interface.FolderMissingException(ex);
            }
        }

        public void Get(string remotename, string localname)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(localname, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, fs);
        }

        public void Get(string remotename, System.IO.Stream output)
        {
            try
            {
                Connection.GetFileStream(m_bucket, this.m_prefix + remotename, output);
            }
            catch
            {
                //Throw original error
                throw;
            }
        }

        public void Delete(string remotename)
        {
            Connection.DeleteObject(m_bucket, this.m_prefix + remotename);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("gs-project", CommandLineArgument.ArgumentType.String,"Project ID","Project ID"),
                    new CommandLineArgument("gs-bucket", CommandLineArgument.ArgumentType.String,"Bucket name","Bucket name"),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String,"Client ID","Client ID"),
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.String,"Client Secret","Client Secret")
                });

            }
        }

        public string Description
        {
            get
            {
                return "Google Cloud Storage";
            }
        }

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            //S3 does not complain if the bucket already exists
            return;
            //Connection.AddBucket(m_bucket);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion


        private GSWrapper Connection
        {
            get { return m_wrapper; }
        }



    }
}
