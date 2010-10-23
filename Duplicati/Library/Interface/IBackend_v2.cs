using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Interface
{
    /// <summary>
    /// This interface extends the bare-minimum backend interface with some common methods
    /// </summary>
    public interface IBackend_v2 : IBackend
    {
        /// <summary>
        /// The purpose of this method is to test the connection to the remote backend.
        /// If any problem is encountered, this method should throw an exception.
        /// If the encountered problem is a missing target &quot;folder&quot;,
        /// this method should throw a <see cref="FolderMissingException"/>.
        /// </summary>
        void Test();

        /// <summary>
        /// The purpose of this method is to create the underlying &quot;folder&quot;.
        /// This method will be invoked if the <see cref="Test"/> method throws a
        /// <see cref="FolderMissingException"/>. 
        /// Backends that have no &quot;folder&quot; concept should not throw
        /// a <see cref="FolderMissingException"/> during <see cref="Test"/>, 
        /// and this method should throw a <see cref="MissingMethodException"/>.
        /// </summary>
        void CreateFolder();

    }
}
