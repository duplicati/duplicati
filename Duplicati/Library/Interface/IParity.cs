using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// Public interface for an parity creation and checking method.
    /// All modules that implements parity function must implement this interface.
    /// The classes that implements this interface MUST also 
    /// implement a default constructor and a constructor that
    /// has the signature new(Dictionary&lt;string, string&gt; options).
    /// The default constructor is used to construct an instance
    /// so the DisplayName and other values can be read.
    /// The other constructor is used to do the actual work.
    /// An instance can be used to create or check parity for multiple files/streams.
    /// It's required that the parity can be generated externally as a single file.
    /// </summary>
    public interface IParity : IDisposable
    {
        /// <summary>
        /// Create parity data for the inputfile, and store them in outputfile.
        /// </summary>
        /// <param name="inputfile">The data file to create parity</param>
        /// <param name="outputfile">The destination of created parity file</param>
        /// <param name="inputname">Some parity provider can also protect file name,
        /// specify this parameter to provide the actual file name to be protected</param>
        void Create(string inputfile, string outputfile, string inputname = null);

        /// <summary>
        /// Fix the input data with the provided parity file.
        /// If output file is not specified, the fix is in-place.
        /// </summary>
        /// <param name="inputfile">The data file to be fixed</param>
        /// <param name="parityfile">The parity file providing information for repairing</param>
        /// <param name="repairedname">The correct file name of the input file identified by the parity file</param>
        /// <param name="outputfile">Optiona output location for fixed data file</param>
        /// <returns>Whether the repair succeeded</returns>
        bool Repair(string inputfile, string parityfile, out string repairedname, string outputfile = null);

        string FilenameExtension { get; }

        /// <summary>
        /// Gets a list of supported commandline arguments
        /// </summary>
        IList<ICommandLineArgument> SupportedCommands { get; }

        /// <summary>
        /// A localized string describing the parity module with a friendly name
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// A localized description of the parity module
        /// </summary>
        string Description { get; }
    }
}
