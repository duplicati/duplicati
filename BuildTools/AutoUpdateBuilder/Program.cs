using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace AutoUpdateBuilder
{
    public class Program
    {
        private static RSACryptoServiceProvider privkey;

        private static string keyfile;
        private static string keyfilepassword;

        private static string gpgkeypassphrase;
        private static string gpgkeyfile;
        private static string gpgpath;
        private static string gpgkeyid;

        private static string outputfolder;

        private static void CompareToManifestPublicKey()
        {
            if (Duplicati.Library.AutoUpdater.AutoUpdateSettings.SignKey == null || privkey.ToXmlString(false) != Duplicati.Library.AutoUpdater.AutoUpdateSettings.SignKey.ToXmlString(false))
            {
                Console.WriteLine("The public key in the project is not the same as the public key from the file");
                Console.WriteLine("Try setting the key to: ");
                Console.WriteLine(privkey.ToXmlString(false));
                System.Environment.Exit(5);
            }
        }

        private static void LoadKeyFromFile()
        {
            using (var enc = new Duplicati.Library.Encryption.AESEncryption(keyfilepassword, new Dictionary<string, string>()))
            using (var ms = new System.IO.MemoryStream())
            using (var fs = System.IO.File.OpenRead(keyfile))
            {
                enc.Decrypt(fs, ms);
                ms.Position = 0;

                using (var sr = new System.IO.StreamReader(ms))
                    privkey.FromXmlString(sr.ReadToEnd());
            }
        }

        private static void SpawnGPG(bool armor)
        {
            var srcfile = System.IO.Path.Combine(outputfolder, "package.zip");

            var armorOption = armor ? "--armor" : "";
            var signatureFileExtension = armor ? "sig.asc" : "sig";
            var gpgArgument = string.Format("--passphrase-fd 0 --batch --yes --default-key={1} {2} --output \"{0}.{3}\" --detach-sig \"{0}\"",
                                            srcfile,
                                            gpgkeyid,
                                            armorOption,
                                            signatureFileExtension);

            var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = gpgpath,
                Arguments = gpgArgument,
                RedirectStandardInput = true,
                UseShellExecute = false
            });

            proc.StandardInput.WriteLine(gpgkeypassphrase);
            proc.WaitForExit();
        }

        private static void SignUsingGPG()
        {
            gpgpath = gpgpath ?? "gpg";
            SpawnGPG(false);
            SpawnGPG(true);
        }

        private static void LoadGPGKeyIdAndPassphrase()
        {
            using (var enc = new Duplicati.Library.Encryption.AESEncryption(keyfilepassword, new Dictionary<string, string>()))
            using (var ms = new System.IO.MemoryStream())
            using (var fs = System.IO.File.OpenRead(gpgkeyfile))
            {
                enc.Decrypt(fs, ms);
                ms.Position = 0;

                // No real format, just two lines
                using (var sr = new System.IO.StreamReader(ms))
                {
                    var lines = sr.ReadToEnd().Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                    gpgkeyid = lines[0];
                    gpgkeypassphrase = lines[1];
                }
            }
        }

        public static int Main(string[] _args)
        {
            var args = new List<string>(_args);
            var opts = Duplicati.Library.Utility.CommandLineParser.ExtractOptions(args);

            string inputfolder;
            string manifestfile;
            string allowNewKey;

            opts.TryGetValue("input", out inputfolder);
            opts.TryGetValue("output", out outputfolder);
            opts.TryGetValue("allow-new-key", out allowNewKey);
            opts.TryGetValue("keyfile", out keyfile);
            opts.TryGetValue("manifest", out manifestfile);
            opts.TryGetValue("keyfile-password", out keyfilepassword);
            opts.TryGetValue("gpgkeyfile", out gpgkeyfile);
			opts.TryGetValue("gpgpath", out gpgpath);

			var usedoptions = new [] { "allow-new-key", "input", "output", "keyfile", "manifest", "keyfile-password", "gpgkeyfile", "gpgpath" };

            if (string.IsNullOrWhiteSpace(inputfolder))
            {
                Console.WriteLine("Missing input folder");
                return 4;
            }

            if (string.IsNullOrWhiteSpace(outputfolder))
            {
                Console.WriteLine("Missing output folder");
                return 4;
            }

            if (string.IsNullOrWhiteSpace(keyfile))
            {
                Console.WriteLine("Missing keyfile");
                return 4;
            }

            if (!System.IO.Directory.Exists(inputfolder))
            {
                Console.WriteLine("Input folder not found");
                return 4;
            }

            if (string.IsNullOrWhiteSpace(keyfilepassword))
            {
                Console.WriteLine("Enter keyfile passphrase: ");
                keyfilepassword = Console.ReadLine().Trim();
            }

            if (!System.IO.File.Exists(keyfile))
            {
                Console.WriteLine("Keyfile not found, creating new");
                var newkey = RSA.Create().ToXmlString(true);
                using (var enc = new Duplicati.Library.Encryption.AESEncryption(keyfilepassword, new Dictionary<string, string>()))
                using (var fs = System.IO.File.OpenWrite(keyfile))
                using (var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(newkey)))
                    enc.Encrypt(ms, fs);
            }

            if (!System.IO.Directory.Exists(outputfolder))
                System.IO.Directory.CreateDirectory(outputfolder);

            privkey = (RSACryptoServiceProvider) RSA.Create();

            LoadKeyFromFile();

            if (!Boolean.TryParse(allowNewKey, out Boolean newKeyAllowed) || !newKeyAllowed)
            {
                CompareToManifestPublicKey();
            }

			if (string.IsNullOrWhiteSpace(gpgkeyfile))
			{
				Console.WriteLine("No gpgfile, skipping GPG signature files");
			}
			else if (!System.IO.File.Exists(gpgkeyfile))
			{
				Console.WriteLine("Missing gpgfile");
				return 6;
			}
			else
			{
                LoadGPGKeyIdAndPassphrase();
            }

            Duplicati.Library.AutoUpdater.UpdateInfo updateInfo;

            using (var fs = System.IO.File.OpenRead(manifestfile))
            using (var sr = new System.IO.StreamReader(fs))
            using (var jr = new Newtonsoft.Json.JsonTextReader(sr))
                updateInfo = new Newtonsoft.Json.JsonSerializer().Deserialize<Duplicati.Library.AutoUpdater.UpdateInfo>(jr);

            var isopts = new Dictionary<string, string>(opts, StringComparer.InvariantCultureIgnoreCase);
            foreach (var usedopt in usedoptions)
            {
                isopts.Remove(usedopt);
            }

            foreach (var k in updateInfo.GetType().GetFields())
            {
                if (!isopts.ContainsKey(k.Name))
                {
                    continue;
                }
                try
                {
                    //Console.WriteLine("Setting {0} to {1}", k.Name, isopts[k.Name]);
                    if (k.FieldType == typeof(string[]))
                        k.SetValue(updateInfo, isopts[k.Name].Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    else if (k.FieldType == typeof(Version))
                        k.SetValue(updateInfo, new Version(isopts[k.Name]));
                    else if (k.FieldType == typeof(int))
                        k.SetValue(updateInfo, int.Parse(isopts[k.Name]));
                    else if (k.FieldType == typeof(long))
                        k.SetValue(updateInfo, long.Parse(isopts[k.Name]));
                    else
                        k.SetValue(updateInfo, isopts[k.Name]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed setting {0} to {1}: {2}", k.Name, isopts[k.Name], ex.Message);
                }

                isopts.Remove(k.Name);
            }

            foreach (var opt in isopts)
            {
                Console.WriteLine("Warning! unused option: {0} = {1}", opt.Key, opt.Value);
            }

            using (var tf = new Duplicati.Library.Utility.TempFile())
            {
                using (var fs = System.IO.File.OpenWrite(tf))
                using (var tw = new System.IO.StreamWriter(fs))
                    new Newtonsoft.Json.JsonSerializer().Serialize(tw, updateInfo);

                Duplicati.Library.AutoUpdater.UpdaterManager.CreateUpdatePackage(privkey, inputfolder, outputfolder, tf);
            }

			if (gpgkeyid != null)
			{
                SignUsingGPG();
			}

            return 0;
        }
    }
}