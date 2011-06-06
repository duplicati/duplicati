using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Server
{
    /// <summary>
    /// The class that keeps a list of all events that occured
    /// </summary>
    public class EventQueue
    {
        /// <summary>
        /// Class that encapsulates all information relating to an event
        /// </summary>
        public class EventData
        {
            /// <summary>
            /// Internal counter for sequence numbers
            /// </summary>
            private static long __EventSeqNo = 0;

            /// <summary>
            /// Gets the sequence number, which can be used for determining the order of events
            /// </summary>
            public long EventSequenceNumber { get; private set; }
            /// <summary>
            /// Gets the time the event occured
            /// </summary>
            public DateTime EventTime { get; private set; }
            /// <summary>
            /// Gets the data related to the event
            /// </summary>
            public object Data { get; private set; }

            /// <summary>
            /// Initializes a new instance of the <see cref="EventData"/> class.
            /// </summary>
            /// <param name="data">The event data</param>
            public EventData(object data)
            {
                this.EventSequenceNumber = __EventSeqNo++;
                this.EventTime = DateTime.Now;

                this.Data = data;
            }
        }


        /// <summary>
        /// The number of events kept in the queue
        /// </summary>
        private const int MAX_EVENTS = 2000;

        /// <summary>
        /// The internal list of known events
        /// </summary>
        private List<EventData> m_events = new List<EventData>();

        /// <summary>
        /// The lock that protects access to the event list
        /// </summary>
        private object m_lock = new object();


        /// <summary>
        /// Gets the list of known events, possibly limited by a start sequence number.
        /// </summary>
        /// <param name="firstSeqNo">The first sequence number to retrieve</param>
        /// <returns>A list with the events</returns>
        public IList<EventData> GetEvents(long firstSeqNo = 0)
        {
            lock (m_lock)
            {
                if (m_events.Count == 0)
                    return new List<EventData>();

                int firstIndex = 0;
                long firstRecordedNo = m_events[0].EventSequenceNumber;

                firstIndex = Math.Min(Math.Max(0, (int)(firstSeqNo - firstRecordedNo)), m_events.Count);

                if (firstIndex == m_events.Count)
                    return new List<EventData>();
                else
                    return m_events.GetRange(firstIndex, m_events.Count - firstIndex);
            }
        }

        public long CurrentEventId { get; private set; }

        public void LiveControl_StateChanged(object sender, EventArgs args)
        {
        }

        public void LiveControl_ThreadPriorityChanged(object sender, EventArgs args)
        {
        }

        public void LiveControl_ThrottleSpeedChanged(object sender, EventArgs args)
        {
        }

        public void WorkThread_StartingWork(object sender, EventArgs args)
        {
        }

        public void WorkThread_CompletedWork(object sender, EventArgs args)
        {
        }

        public void WorkThread_WorkQueueChanged(object sender, EventArgs args)
        {
        }

        public void Scheduler_NewSchedule(object sender, EventArgs args)
        {
        }

        public void Runner_DuplicatiProgress(Duplicati.Library.Main.DuplicatiOperation operation, Duplicati.Server.DuplicatiRunner.RunnerState state, string message, string submessage, int progress, int subprogress)
        {
        }

        public void DataConnection_AfterDataConnection(object sender, System.Data.LightDatamodel.DataActions args)
        {
        }

    }
}
