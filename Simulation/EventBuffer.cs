using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    enum EventType
    {
        NetLinkConnected,
        NetLinkDisconnected,
        NodeAdded,
        NodeRemoved,
        NetworkMessageLost,
        NetworkMessageTransmitted
    }
    struct SimulationEvent
    {
        public EventType Type;
        public String ObjectName;
        public String Message;
    }
    class EventBuffer : IDisposable
    {
        private Queue<SimulationEvent> EventQueue;
        private readonly Object EventQueue_Lock = new Object();
        private StreamWriter OutputStream;
        private readonly Object OutputStream_Lock = new Object();
        private Thread OutputThread;
        private DateTime LastFlush;

        public bool Run { get; private set; }
        public int BufferedEvents
        {
            get
            {
                lock(EventQueue_Lock)
                {
                    return EventQueue.Count;
                }
            }
        }

        public EventBuffer()
        {
            EventQueue = new Queue<SimulationEvent>();
            LastFlush = DateTime.Now;
            OutputThread = new Thread(OutputWriterWorker);
            Run = true;
            OutputThread.Start();
        }

        public void InitOutputFile(String filename)
        {
            lock (OutputStream_Lock)
            {
                if (OutputStream != null)
                {
                    OutputStream.Flush();
                    OutputStream.Close();
                }
                OutputStream = new StreamWriter(new FileStream(filename, FileMode.Create, FileAccess.Write));
            }
        }

        public void Stop()
        {
            Run = false;
            if(OutputThread != null)
            {
                OutputThread.Join();
                OutputThread = null;
            }
        }

        public void AddEvent(SimulationEvent evt)
        {
            lock (EventQueue_Lock)
            {
                EventQueue.Enqueue(evt);
            }
        }

        public void OutputWriterWorker()
        {
            while(Run)
            {
                while(EventQueue.Count > 0)
                {
                    SimulationEvent evt;
                    lock (EventQueue_Lock) {
                        evt = EventQueue.Dequeue();
                    }
                    String logmessage = String.Format("{0}| {1} - ({2}): {3}",
                        SimulationController.Instance.CurrentTick, // tick
                        evt.Type.ToString(),
                        evt.ObjectName,
                        evt.Message);
                    lock(OutputStream_Lock)
                    {
                        if(OutputStream != null)
                        {
                            OutputStream.WriteLine(logmessage);
                            if((DateTime.Now-LastFlush).TotalSeconds > 1.0)
                            {
                                OutputStream.Flush();
                                LastFlush = DateTime.Now;
                            }
                        }
                    }
                    System.Diagnostics.Trace.WriteLine(logmessage);
                }
            }
        }

        public void Dispose()
        {
            ((IDisposable)OutputStream).Dispose();
        }
    }
}
