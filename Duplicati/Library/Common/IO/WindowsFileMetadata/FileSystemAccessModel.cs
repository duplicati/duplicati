using Newtonsoft.Json;
using System.Security.AccessControl;

namespace Duplicati.Library.IO.WindowsFileMetadata
{
    public class FileSystemAccessModel
    {
        // Use JsonProperty Attribute to allow readonly fields to be set by deserializer
        // https://github.com/duplicati/duplicati/issues/4028
        [JsonProperty]
        public readonly FileSystemRights Rights;
        [JsonProperty]
        public readonly AccessControlType ControlType;
        [JsonProperty]
        public readonly string SID;
        [JsonProperty]
        public readonly bool Inherited;
        [JsonProperty]
        public readonly InheritanceFlags Inheritance;
        [JsonProperty]
        public readonly PropagationFlags Propagation;

        public FileSystemAccessModel()
        {
        }

        public FileSystemAccessModel(FileSystemAccessRule rule)
        {
            Rights = rule.FileSystemRights;
            ControlType = rule.AccessControlType;
            SID = rule.IdentityReference.Value;
            Inherited = rule.IsInherited;
            Inheritance = rule.InheritanceFlags;
            Propagation = rule.PropagationFlags;
        }

        public FileSystemAccessRule Create(System.Security.AccessControl.FileSystemSecurity owner)
        {
            return (FileSystemAccessRule)owner.AccessRuleFactory(
                new System.Security.Principal.SecurityIdentifier(SID),
                (int)Rights,
                Inherited,
                Inheritance,
                Propagation,
                ControlType);
        }
    }
}
