// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

//TODO: Use the IPGlobalProperties to dynamically throttle data
//http://msdn.microsoft.com/en-us/library/system.net.networkinformation.ipglobalproperties.aspx

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// This class throttles the rate data can be read or written to the underlying stream.
    /// This creates a bandwidth throttle option for any stream, including a network stream.
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
		/// The time the last read was sampled
		/// </summary>
		private DateTime m_last_read_sample;
		/// <summary>
		/// The bytes-read counter for this period
		/// </summary>
		private long m_current_read_counter;
		/// <summary>
		/// The current measured read speed
		/// </summary>
		private double m_current_read_speed;

		/// <summary>
		/// The time the last read was sampled
		/// </summary>
		private DateTime m_last_write_sample;
		/// <summary>
		/// The bytes-written counter for this period
		/// </summary>
		private long m_current_write_counter;
		/// <summary>
		/// The current measured read speed
		/// </summary>
		private double m_current_write_speed;

		/// <summary>
		/// The time the last read was sampled
		/// </summary>
		private DateTime m_last_limit_update;

		/// <summary>
		/// The number of ticks to have passed before a sample is taken
		/// </summary>
		private const long SAMPLE_PERIOD = TimeSpan.TicksPerSecond / 4;

		/// <summary>
		/// The number of ticks to have passed before the limits updater is called
		/// </summary>
		private const long UPDATE_PERIOD = TimeSpan.TicksPerSecond;

		/// <summary>
		/// Callback method used to update limits
		/// </summary>
		private readonly Action<ThrottledStream> m_updateLimits;

		/// <summary>
		/// Creates a throttle around a stream.
		/// </summary>
		/// <param name="basestream">The stream to throttle</param>
        public ThrottledStream(Stream basestream, Action<ThrottledStream> updateLimits)
			: base(basestream)
		{
            m_last_read_sample = m_last_write_sample = m_last_limit_update = new DateTime(0);
            m_updateLimits = updateLimits;
		}

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

			m_last_read_sample = m_last_write_sample = m_last_limit_update = new DateTime(0);
            m_updateLimits = null;
        }

		/// <summary>
		/// Read from this stream into a buffer.
		/// </summary>
		/// <param name="buffer">The buffer to write to.</param>
		/// <param name="offset">The offset into the buffer.</param>
		/// <param name="count">The number of bytes to read.</param>
        public override int Read(byte[] buffer, int offset, int count)
        {
			var remaining = count;

			while (remaining > 0)
			{
				// To avoid excessive waiting, the delay will wait at most 2 seconds,
				// so we split the blocks to limit the number of seconds we can wait
				UpdateLimits();
				var chunksize = (int)Math.Min(remaining, m_readspeed <= 0 ? remaining : m_readspeed * 2);
				DelayIfRequired(ref m_readspeed, chunksize, ref m_last_read_sample, ref m_current_read_counter, ref m_current_read_speed);

				var actual = m_basestream.Read(buffer, offset + count - remaining, chunksize);

				if (actual <= 0)
					break;

				m_current_read_counter += actual;

				remaining -= actual;
			}

			return count - remaining;
        }

		/// <summary>
		/// Write from a buffer to this stream.
		/// </summary>
		/// <param name="buffer">The buffer to read from.</param>
		/// <param name="offset">The offset into the buffer.</param>
		/// <param name="count">The number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count)
		{
			int bytesWritten = 0;
			while (count > 0)
			{
                // To avoid excessive waiting, the delay will wait at most 2 seconds,
                // so we split the blocks to limit the number of seconds we can wait
                UpdateLimits();
				var chunksize = (int)Math.Min(count, m_writespeed <= 0 ? count : m_writespeed * 2);
				DelayIfRequired(ref m_writespeed, chunksize, ref m_last_write_sample, ref m_current_write_counter, ref m_current_write_speed);
				m_basestream.Write(buffer, offset + bytesWritten, chunksize);

				m_current_write_counter += chunksize;
				bytesWritten += chunksize;
				count -= chunksize;
			}
        }

        /// <summary>
        /// Gets or sets the current read speed in bytes pr. second.
        /// Set to zero or less to disable throttling.
        /// </summary>
        public long ReadSpeed
        {
            get { return m_readspeed; }
            set { m_readspeed = value; }
        }

        /// <summary>
        /// Gets or sets the current write speed in bytes pr. second.
        /// Set to zero or less to disable throttling
        /// </summary>
        public long WriteSpeed
        {
            get { return m_writespeed; }
            set { m_writespeed = value; }
        }

		/// <summary>
		/// Gets the actual measured read speed.
		/// </summary>
		public double MeasuredReadSpeed { get { return m_current_read_speed; } }

		/// <summary>
		/// Gets the actual measured write speed.
		/// </summary>
		public double MeasuredWriteSpeed { get { return m_current_write_speed; } }

        /// <summary>
        /// Helper method called to update the limits
        /// </summary>
        private void UpdateLimits()
        {
            var now = DateTime.Now;
            if (m_updateLimits != null && now.Ticks - m_last_limit_update.Ticks > UPDATE_PERIOD)
            {
                m_updateLimits(this);
                m_last_limit_update = now;
            }
        }

		/// <summary>
		/// Calculates the speed, and inserts appropriate delays
		/// </summary>
		private void DelayIfRequired(ref long limit, int count, ref DateTime last_sample, ref long last_count, ref double current_speed)
		{
			var now = DateTime.Now;

			if (count <= 0 || limit <= 0)
				return;

			// If we are just starting, set the timer and counter
			if (last_sample.Ticks == 0)
			{
				last_count = 0;
				last_sample = now;
				current_speed = limit;
				return;
			}

			// Compute the current duration
			var duration = now - last_sample;

			// Update speed in intervals
			if (duration.Ticks > SAMPLE_PERIOD || last_count > limit)
			{
				// After a sample period, measure how far ahead we are
				var target_delay = TimeSpan.FromSeconds(last_count / (double)limit) - duration;

				// If we are actually ahead, delay for a little while
				if (target_delay.Ticks > 1000)
				{
					// With large changes, we avoid sleeping for several minutes
					// This makes the throttling more responsive when increasing the
					// throughput, even with large changes
					var ms = (int)Math.Min(target_delay.TotalMilliseconds, 2 * 1000);
					System.Threading.Thread.Sleep(ms);

					// When we compute how fast this sample was, we include the delay
					now = DateTime.Now;
				}

				current_speed = last_count / (now - last_sample).TotalSeconds;
				last_sample = now;
				last_count = 0;
			}
        }
    }
}
