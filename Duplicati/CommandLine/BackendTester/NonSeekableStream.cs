using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.CommandLine.BackendTester
{
    /// <summary>
    /// This class prevents the base stream from being seekable to 
    /// ensure that all operations work correctly on streams that
    /// are not seekable.
    /// </summary>
    public class NonSeekableStream : Library.Core.OverrideableStream
    {
        public NonSeekableStream(System.IO.Stream stream)
            : base(stream)
        { }

        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
    }
}
