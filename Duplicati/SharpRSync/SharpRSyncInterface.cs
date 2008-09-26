using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Duplicati.Main
{
    /// <summary>
    /// This class wraps the usage of SharpRSync into an easy to use static class
    /// </summary>
    public class SharpRSyncInterface
    {
        /// <summary>
        /// Generates a signature, and writes it to the stream
        /// </summary>
        /// <param name="input">The stream to create the signature for</param>
        /// <param name="output">The stream to write the signature into</param>
        public static void GenerateSignature(Stream input, Stream output)
        {
            SharpRSync.ChecksumFile cs = new Duplicati.SharpRSync.ChecksumFile();
            cs.AddStream(input);
            cs.Save(output);
        }

        /// <summary>
        /// Generates a signature file
        /// </summary>
        /// <param name="filename">The file to create the signature for</param>
        /// <param name="outputfile">The file write the signature to</param>
        public static void GenerateSignature(string filename, string outputfile)
        {
            if (System.IO.File.Exists(outputfile))
                System.IO.File.Delete(outputfile);

            using (FileStream fs1 = File.OpenRead(filename))
            using (FileStream fs2 = File.Create(outputfile))
                GenerateSignature(fs1, fs2);
        }

        /// <summary>
        /// Generates a delta stream
        /// </summary>
        /// <param name="signature">The signature for the stream</param>
        /// <param name="filename">The (possibly) altered stream to create the delta for</param>
        /// <param name="output">The delta output</param>
        public static void GenerateDelta(Stream signature, Stream input, Stream output)
        {
            SharpRSync.ChecksumFile cs = new Duplicati.SharpRSync.ChecksumFile(signature);
            SharpRSync.DeltaFile df = new Duplicati.SharpRSync.DeltaFile(cs);
            df.GenerateDeltaFile(input, output);
        }


        /// <summary>
        /// Generates a delta file
        /// </summary>
        /// <param name="signaturefile">The signature for the file</param>
        /// <param name="filename">The file to create the delta for</param>
        /// <param name="deltafile">The delta output file</param>
        public static void GenerateDelta(string signaturefile, string filename, string deltafile)
        {
            if (System.IO.File.Exists(deltafile))
                System.IO.File.Delete(deltafile);

            using (FileStream fs1 = File.OpenRead(signaturefile))
            using (FileStream fs2 = File.OpenRead(filename))
            using (FileStream fs3 = File.Create(deltafile))
                GenerateDelta(fs1, fs2, fs3);
        }

        /// <summary>
        /// Patches a file
        /// </summary>
        /// <param name="basefile">The most recent full copy of the file</param>
        /// <param name="deltafile">The delta file</param>
        /// <param name="outputfile">The restored file</param>
        public static void PatchFile(string basefile, string deltafile, string outputfile)
        {
            if (System.IO.File.Exists(outputfile))
                System.IO.File.Delete(outputfile);

            using (FileStream fs1 = File.OpenRead(basefile))
            using (FileStream fs2 = File.OpenRead(deltafile))
            using (FileStream fs3 = File.Create(outputfile))
                PatchFile(fs1, fs2, fs3);
        }

        /// <summary>
        /// Constructs a stream from a basestream and a delta stream
        /// </summary>
        /// <param name="basefile">The most recent full copy of the file</param>
        /// <param name="deltafile">The delta file</param>
        /// <param name="outputfile">The restored file</param>
        public static void PatchFile(Stream basestream, Stream delta, Stream output)
        {
            SharpRSync.DeltaFile df = new Duplicati.SharpRSync.DeltaFile(delta);
            df.PatchFile(basestream, output);
        }

    }
}
