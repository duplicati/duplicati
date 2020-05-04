using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Tardigrade
{
    public class Tardigrade : IBackend
    {
        private const string TARDIGRADE_SATELLITE = "tardigrade-satellite";
        private const string TARDIGRADE_API_KEY = "tardigrade-api-key";
        private const string TARDIGRADE_SECRET = "tardigrade-secret";
        private const string TARDIGRADE_SHARED_ACCESS = "tardigrade-shared-access";

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Tardigrade()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public Tardigrade(string url, Dictionary<string, string> options)
        {
        }

        public string DisplayName
        {
            get { return Strings.Tardigrade.DisplayName; }
        }

        public string ProtocolKey => "tardigrade";

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(TARDIGRADE_SATELLITE, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeSatelliteDescriptionShort, Strings.Tardigrade.TardigradeSatelliteDescriptionShort, "us-central-1.tardigrade.io:7777"),
                    new CommandLineArgument(TARDIGRADE_API_KEY, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeAPIKeyDescriptionShort, Strings.Tardigrade.TardigradeAPIKeyDescriptionLong),
                    new CommandLineArgument(TARDIGRADE_SECRET, CommandLineArgument.ArgumentType.Password, Strings.Tardigrade.TardigradeSecretDescriptionShort, Strings.Tardigrade.TardigradeSecretDescriptionLong),
                    new CommandLineArgument(TARDIGRADE_SHARED_ACCESS, CommandLineArgument.ArgumentType.String, Strings.Tardigrade.TardigradeSharedAccessDescriptionShort, Strings.Tardigrade.TardigradeSharedAccessDescriptionLong),
                });
            }
        }

        public string Description
        {
            get
            {
                return Strings.Tardigrade.Description;
            }
        }

        public string[] DNSName => throw new NotImplementedException();

        public void CreateFolder()
        {
            throw new NotImplementedException();
        }

        public void Delete(string remotename)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public void Get(string remotename, string filename)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<IFileEntry> List()
        {
            throw new NotImplementedException();
        }

        public Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            throw new NotImplementedException();
        }

        public void Test()
        {
            throw new NotImplementedException();
        }
    }
}
