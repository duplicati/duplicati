using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Duplicati.Server.Serialization;

namespace Duplicati.Server.Serializable
{
    public class JobSettings : IJobSettings
    {
        /// <summary>
        /// Constructs a new job settings object with default values from xml files
        /// </summary>
        public JobSettings()
        {
            this.Initialize();
            Duplicati.Datamodel.ApplicationSettings appset = new Duplicati.Datamodel.ApplicationSettings(Program.DataConnection);
            if (appset.UseCommonPassword)
            {
                //TODO: Set this, but do not transfer to client ?
                //this.BackupPassword = appset.CommonPassword;
                this.EncryptionModule = appset.CommonPasswordEncryptionModule;
            }

            ApplyXDoc(XDocument.Load(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(Program), "backup defaults.xml")));
            string extraPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "backup defaults.xml");
            if (System.IO.File.Exists(extraPath))
                ApplyXDoc(XDocument.Load(extraPath));

            //Since this element is not attached to any database element, set the id to -1
            this.Id = -1;
        }

        private void ApplyXDoc(XDocument settings)
        {
            var el = settings.Element("settings");
            if (el == null)
                return;

            foreach(var p in el.Elements())
            {
                var prop = this.GetType().GetProperty(p.Name.LocalName, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (prop == null)
                    continue;

                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(this, p.Value, null);
                }
                else if (prop.PropertyType == typeof(bool))
                {
                    prop.SetValue(this, Duplicati.Library.Utility.Utility.ParseBool(p.Value, false), null);
                }
                else if (prop.PropertyType == typeof(IList<string>))
                {
                    //Use any element name, usually 
                    //  <value>x</value> 
                    //  <value>y</value>
                    //but also accept:
                    //  <key value="x" />
                    //  <key value="y" />
                    //or any mix of that
                    prop.SetValue(this, p.Elements().Select(x =>
                    {
                        if (x.Attribute("value") != null)
                            return x.Attribute("value").Value;
                        else
                            return x.Value;
                    }).ToList(), null);
                }
                else if (prop.PropertyType == typeof(IDictionary<string, string>))
                {
                    IDictionary<string, string> props = (IDictionary<string, string>)prop.GetValue(this, null);
                    foreach (var pe in p.Elements())
                    {
                        //using the <key name="xx" value="yy" /> format
                        if (pe.Name.LocalName == "key" && pe.Attribute("name") != null && pe.Attribute("value") != null)
                            props[pe.Attribute("name").Value] = pe.Attribute("value").Value;
                        //using the <name>value</name> format
                        else
                            props[pe.Name.LocalName] = pe.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Constructs a new job settings object with values reflecting an existing schedule
        /// </summary>
        /// <param name="schedule"></param>
        public JobSettings(Duplicati.Datamodel.Schedule schedule)
        {
            this.Initialize();
            throw new MissingMethodException();
        }


        protected void Initialize()
        {
            this.Labels = new List<string>();
            this.Settings = new Dictionary<string, string>();
            this.Overrides = new Dictionary<string, string>();
            this.SchedulerSettings = new Dictionary<string, string>();
            this.BackendSettings = new BackendSettings();
            this.CompressionSettings = new Dictionary<string, string>();
            this.EncryptionSettings = new Dictionary<string, string>();
            this.FilterSets = new List<IFilterSet>();
        }

        public long Id { get; set; }
        public string Name { get; set; }
        public IList<string> Labels { get; set; }
        public IList<string> SourcePaths { get; set; }
        public IList<IFilterSet> FilterSets { get; set; }
        public bool IncludeSetup { get; set; }
        public string BackendModule { get; set; }
        public string CompressionModule { get; set; }
        public string EncryptionModule { get; set; }
        public IDictionary<string, string> Settings { get; set; }
        public IDictionary<string, string> Overrides { get; set; }
        public IDictionary<string, string> SchedulerSettings { get; set; }
        public IBackendSettings BackendSettings { get; set; }
        public IDictionary<string, string> CompressionSettings { get; set; }
        public IDictionary<string, string> EncryptionSettings { get; set; }

    }
}
