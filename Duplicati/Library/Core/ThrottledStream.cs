using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

//TODO: Use the IPGlobalProperties to dynamically throttle data
//http://msdn.microsoft.com/en-us/library/system.net.networkinformation.ipglobalproperties.aspx

namespace Duplicati.Library.Core
{
    /// <summary>
    /// This class throttles the rate data can be read or written to the underlying stream.
    /// This creates a bandwith throttle option for any stream, including a network stream.
    /// </summary>
    public class ThrottledStream : OverrideableStream
    {
        /// <summary>
        /// The max number of bytes pr. second to write
        /// </summary>
        private long m_writespeed;
        /// <summary>
        /// The max number of bytes pr. second to read
        /// </summary>
        private long m_readspeed;

        /// <summary>
        /// This is a list of the most recent reads. The key is the tick at the time, and the value is the number of bytes.
        /// </summary>
        List<KeyValuePair<long, long>> m_dataread;
        /// <summary>
        /// This is a list of the most recent writes. The key is the tick at the time, and the value is the number of bytes.
        /// </summary>
        List<KeyValuePair<long, long>> m_datawritten;
        /// <summary>
        /// This is the sum of all bytes in the m_dataread table, summed for fast access.
        /// </summary>
        private long m_bytesread;
        /// <summary>
        /// This is the sum of all bytes in the m_datawritten table, summed for fast access.
        /// </summary>
        private long m_byteswritten;

        /// <summary>
        /// The number of reads or writes to keep track of
        /// </summary>
        private const long STATISTICS_SIZE = 500;
        /// <summary>
        /// The number of ticks to have passed before the throttle begins
        /// </summary>
        private const long MIN_DURATION = TimeSpan.TicksPerSecond / 4;
        /// <summary>
        /// The number of sub chunks to perform when throttling
        /// </summary>
        private const int DELAY_CHUNK_SIZE = 1024;

        /// <summary>
        /// Creates a throttle around a stream.
        /// </summary>
        /// <param name="basestream">The stream to throttle</param>
        /// <param name="readspeed">The maximum number of bytes pr. second to read. Specify a number less than 1 to allow unlimited speed.</param>
        /// <param name="writespeed">The maximum number of bytes pr. second to write. Specify a number less than 1 to allow unlimited speed.</param>
        public ThrottledStream(Stream basestream, long readspeed, long writespeed)
            : base(basestream)
        {
            m_readspeed = readspeed;
            m_writespeed = writespeed;

            if (m_basestream.CanRead && m_readspeed > 0)
                m_dataread = new List<KeyValuePair<long, long>>();
            if (m_basestream.CanWrite && m_writespeed > 0)
                m_datawritten = new List<KeyValuePair<long, long>>();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = DelayIfRequired(true, buffer, ref offset, ref count);
            if (count == 0)
                return bytesRead;
            else
                return m_basestream.Read(buffer, offset, count) + bytesRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            DelayIfRequired(true, buffer, ref offset, ref count);
            if (count > 0)
                m_basestream.Write(buffer, offset, count);
        }

        /// <summary>
        /// Calculates the speed, and inserts appropriate delays
        /// </summary>
        /// <param name="read">True if the operation is read, false otherwise</param>
        /// <param name="buffer">The data buffer</param>
        /// <param name="offset">The offset into the buffer</param>
        /// <param name="count">The number of bytes to read or write</param>
        /// <returns>The number of bytes processed while delaying</returns>
        private int DelayIfRequired(bool read, byte[] buffer, ref int offset, ref int count)
        {
            if (count <= 0)
                return 0;

            List<KeyValuePair<long, long>> table = read ? m_dataread : m_datawritten;
            int bytesprocessed = 0;

            if (table != null)
            {
                long maxspeed = read ? m_readspeed : m_writespeed;
                Stream stream = m_basestream;
                long bytecount = read ? m_bytesread : m_byteswritten;

                //Add this access
                table.Add(new KeyValuePair<long,long>(DateTime.Now.Ticks, count));
                bytecount += count;

                //Prevent too large tables
                while (table.Count > STATISTICS_SIZE)
                {
                    bytecount -= table[0].Value;
                    table.RemoveAt(0);
                }

                if (table.Count != 0 && bytecount != 0)
                {
                    TimeSpan duration = new TimeSpan(table[table.Count - 1].Key - table[0].Key);

                    //Bail if we are too slow
                    if (duration.Ticks < MIN_DURATION || bytecount <= 0)
                        return 0;

                    //TODO: The resolution is too low in "TotalSeconds", so the speed is a little higher
                    double speed = bytecount / duration.TotalSeconds;
                    if (speed > maxspeed)
                    {
                        //We are too fast, delay the access. Calculating how much wait we need.
                        double secondsNeeded = (bytecount / (double)maxspeed) - duration.TotalSeconds;
                        long delayTicks = (long)(secondsNeeded * TimeSpan.TicksPerSecond);

                        //Calculate the time we should finish, to obey the limit
                        long targetTime = DateTime.Now.Ticks + delayTicks;

                        if (delayTicks > 0)
                        {
                            while (count > DELAY_CHUNK_SIZE && delayTicks > 0)
                            {
                                int bytes = read ? stream.Read(buffer, offset, DELAY_CHUNK_SIZE) : DELAY_CHUNK_SIZE;
                                if (!read)
                                    stream.Write(buffer, offset, DELAY_CHUNK_SIZE);

                                delayTicks = targetTime - DateTime.Now.Ticks;
                                long ticksToDelay = (delayTicks / count) * bytes;

                                if (ticksToDelay > 0)
                                    System.Threading.Thread.Sleep(new TimeSpan(ticksToDelay));

                                //Reset to include the waited time
                                delayTicks = targetTime - DateTime.Now.Ticks;

                                offset += bytes;
                                count -= bytes;
                                bytesprocessed += bytes;

                                if (bytes == 0)
                                    break;
                            }

                            if (delayTicks > 0)
                                System.Threading.Thread.Sleep(new TimeSpan(delayTicks));

                            //Add a marker, indicating that we already slowed down
                            table.Add(new KeyValuePair<long, long>(DateTime.Now.Ticks, 0));
                        }

                    }
                }

                if (read)
                    m_bytesread = bytecount;
                else
                    m_byteswritten = bytecount;
            }

            return bytesprocessed;
        }
    }
}
