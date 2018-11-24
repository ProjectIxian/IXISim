using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    enum ThreadOp
    {
        UpdateLinks,
        UpdateNodes,
        Terminate
    }
    struct ThreadJob
    {
        public int ThreadId;
        public ThreadOp OP;
        public int StartObj;
        public int EndObj;
        public bool Done;
    }

    class SimulationController
    {
        // Singleton
        private static SimulationController _Instance;
        public static SimulationController Instance
        {
            get
            {
                if(_Instance == null)
                {
                    _Instance = new SimulationController();
                }
                return _Instance;
            }
        }
        // Management
        private Thread MasterThread;
        private Thread[] SimulationThreads;
        private List<ThreadJob> AssignedJobs;
        private bool OpStarted;
        public bool Running { get; private set; }

        // Time
        private bool PerformTick;
        public ulong CurrentTick { get; private set; }
        public double TickSeconds { get; private set; }
        // Objects
        private ulong NextID;
        private readonly Object NextID_lock = new Object();
        private readonly Object Simulation_lock = new Object();
        private Dictionary<ulong, IXINode> SimulatedNodes;
        private Dictionary<ulong, IXILink> SimulatedLinks;
        // Upkeep stuff
        private readonly Object DeadLinks_Lock = new Object();
        private Queue<ulong> DeadLinks;
        private readonly Object DeadNodes_Lock = new Object();
        private Queue<ulong> DeadNodes;
        private readonly Object NewLinks_Lock = new Object();
        private Queue<IXILink> NewLinks;
        private readonly Object NewNodes_Lock = new Object();
        private Queue<IXINode> NewNodes;
        // Misc
        public Random RNG;
        public EventBuffer Events { get; private set; }

        private SimulationController()
        {
            CurrentTick = 0;
            TickSeconds = 1.0;
            NextID = 1;
            RNG = new Random();
            Events = new EventBuffer();
            OpStarted = false;
            SimulatedNodes = new Dictionary<ulong, IXINode>();
            SimulatedLinks = new Dictionary<ulong, IXILink>();
            DeadLinks = new Queue<ulong>();
            DeadNodes = new Queue<ulong>();
            NewLinks = new Queue<IXILink>();
            NewNodes = new Queue<IXINode>();
        }

        public void FixRandom(int seed)
        {
            RNG = new Random(seed);
        }

        public ulong GetNextID()
        {
            lock(NextID_lock)
            {
                ulong nid = NextID;
                NextID += 1;
                return nid;
            }
        }

        public double TicksToSeconds(ulong ticks)
        {
            return ticks * TickSeconds;
        }

        public ulong SecondsToTicks(double seconds)
        {
            return (ulong)(seconds / (double)TickSeconds);
        }

        public void SetSimulationOutputFile(String filename)
        {
            Events.InitOutputFile(filename);
        }

        public void StartSimulation(int num_threads)
        {
            if(Running)
            {
                throw new Exception("Simulation is already running!");
            }
            Running = true;
            OpStarted = false;
            PerformTick = false;
            SimulationThreads = new Thread[num_threads];
            AssignedJobs = new List<ThreadJob>(num_threads);
            for(int i=0;i<num_threads;i++)
            {
                SimulationThreads[i] = new Thread(WorkerThreadLoop);
                SimulationThreads[i].Start();
            }
            MasterThread = new Thread(MasterThreadLoop);
            MasterThread.Start();
        }

        public void StopSimulation()
        {
            Running = false;
            if(MasterThread != null)
            {
                MasterThread.Join();
                MasterThread = null;
            }
        }

        public void Tick()
        {
            if (Running)
            {
                PerformTick = true;
            }
        }

        public void OutputNetwork(String filename)
        {
            if(Running)
            {
                return;
            }
            StreamWriter outFile = File.CreateText(filename);
            outFile.WriteLine("IXISim simulation state:");
            outFile.WriteLine("==================== Network description ====================");
            outFile.WriteLine("Nodes:");
            foreach(ulong id in SimulatedNodes.Keys)
            {
                outFile.WriteLine(String.Format("-> {0}: {1}", id, SimulatedNodes[id].GetDescription()));
            }
            outFile.WriteLine("-------------------------------------------------------------");
            outFile.WriteLine("Links:");
            foreach(ulong id in SimulatedLinks.Keys)
            {
                outFile.WriteLine(String.Format("-> {0}: {1}", id, SimulatedLinks[id].GetDescription()));
            }
            outFile.WriteLine("==================== Network state ==========================");
            foreach(IXINode n in SimulatedNodes.Values)
            {
                outFile.WriteLine("Node {0}:");
                outFile.WriteLine(n.GetState(3));
            }
        }

        public IXINode[] GetOutgoingNeighbors(ulong node_id)
        {
            lock (Simulation_lock)
            {
                return SimulatedLinks.Values.Where(x => x.From == node_id).Select(l => SimulatedNodes[l.To]).ToArray();
            }
        }

        public IXINode[] GetIncomingNeighbors(ulong node_id)
        {
            lock (Simulation_lock)
            {
                return SimulatedLinks.Values.Where(x => x.To == node_id).Select(l => SimulatedNodes[l.From]).ToArray();
            }
        }

        public void AddNode(IXINode n)
        {
            lock(NewNodes_Lock)
            {
                NewNodes.Enqueue(n);
            }
        }

        public void AddLink(IXILink l)
        {
            lock(NewLinks_Lock)
            {
                NewLinks.Enqueue(l);
            }
        }

        public void DropLink(ulong id)
        {
            lock (DeadLinks_Lock)
            {
                DeadLinks.Enqueue(id);
            }
        }

        public void DropNode(ulong id)
        {
            lock (DeadNodes_Lock)
            {
                DeadNodes.Enqueue(id);
            }
        }

        public bool NodeExists(ulong id)
        {
            return SimulatedNodes.ContainsKey(id);
        }

        public IXINode GetNode(ulong id)
        {
            return SimulatedNodes[id];
        }

        public IXILink GetLink(ulong id)
        {
            return SimulatedLinks[id];
        }

        public IXILink GetLink(ulong node_from, ulong node_to)
        {
            return SimulatedLinks.Values.Where(x => x.From == node_from && x.To == node_to).First();
        }

        private void MasterThreadLoop()
        {
            while (true)
            {
                if(PerformTick == false)
                {
                    Thread.Sleep(1);
                    continue;
                }
                PerformTick = false;
                CurrentTick = CurrentTick + 1;
                UpdateLinks();
                UpdateNodes();
                Upkeep();
                if(Running == false)
                {
                    foreach(Thread t in SimulationThreads)
                    {
                        ThreadJob j = new ThreadJob
                        {
                            OP = ThreadOp.Terminate,
                            Done = false
                        };
                        AssignedJobs.Add(j);
                    }
                    OpStarted = true;
                    while (AssignedJobs.Exists(x => x.Done == false))
                    {
                        Thread.Sleep(1);
                    }
                    OpStarted = false;
                    foreach (Thread t in SimulationThreads)
                    {
                        t.Join();
                    }
                    SimulationThreads = null;
                    break;
                }
            }
        }

        private void Upkeep()
        {
            lock (Simulation_lock)
            {
                lock (DeadLinks_Lock)
                {
                    while (DeadLinks.Count > 0)
                    {
                        ulong dl = DeadLinks.Dequeue();
                        IXILink l = SimulatedLinks[dl];
                        Events.AddEvent(new SimulationEvent()
                        {
                            Type = EventType.NetLinkDisconnected,
                            ObjectName = l.GetDescription(),
                            Message = String.Format("Network link disconnected: ({0} -|-> {1})", l.From, l.To)
                        });
                        SimulatedLinks.Remove(dl);
                    }
                }
                lock (DeadNodes_Lock)
                {
                    while (DeadNodes.Count > 0)
                    {
                        ulong dn = DeadNodes.Dequeue();
                        IXINode n = SimulatedNodes[dn];
                        Events.AddEvent(new SimulationEvent()
                        {
                            Type = EventType.NodeRemoved,
                            ObjectName = n.GetDescription(),
                            Message = "Node went offline."
                        });
                        SimulatedNodes.Remove(dn);
                    }
                }
                lock (NewLinks_Lock)
                {
                    while (NewLinks.Count > 0)
                    {
                        IXILink l = NewLinks.Dequeue();
                        Events.AddEvent(new SimulationEvent()
                        {
                            Type = EventType.NetLinkConnected,
                            ObjectName = l.GetDescription(),
                            Message = String.Format("Network link connected: ({0} ---> {1})", l.From, l.To)
                        });
                        SimulatedLinks.Add(l.ID, l);
                    }
                }
                lock(NewNodes_Lock)
                {
                    while(NewNodes.Count>0)
                    {
                        IXINode n = NewNodes.Dequeue();
                        Events.AddEvent(new SimulationEvent()
                        {
                            Type = EventType.NodeAdded,
                            ObjectName = n.GetDescription(),
                            Message = "Node came online."
                        });
                        SimulatedNodes.Add(n.ID, n);
                    }
                }
            }
        }

        private void UpdateLinks()
        {
            int num_per_thread = (SimulatedLinks.Count / SimulationThreads.Count()) + 1;
            int current_idx = 0;
            foreach (Thread t in SimulationThreads)
            {
                ThreadJob j = new ThreadJob
                {
                    ThreadId = t.ManagedThreadId,
                    Done = false,
                    OP = ThreadOp.UpdateLinks,
                    StartObj = current_idx,
                    EndObj = current_idx + num_per_thread
                };
                if (j.EndObj >= SimulatedLinks.Count)
                {
                    j.EndObj = SimulatedLinks.Count - 1;
                }
                current_idx = j.EndObj + 1;
                AssignedJobs.Add(j);
            }
            OpStarted = true;
            // wait until all threads are finished
            while (AssignedJobs.Exists(x => x.Done == false))
            {
                Thread.Sleep(1);
            }
            AssignedJobs.Clear();
            OpStarted = false;
        }

        private void UpdateNodes()
        {
            int num_per_thread = (SimulatedNodes.Count / SimulationThreads.Count()) + 1;
            int current_idx = 0;
            foreach (Thread t in SimulationThreads)
            {
                ThreadJob j = new ThreadJob
                {
                    ThreadId = t.ManagedThreadId,
                    Done = false,
                    OP = ThreadOp.UpdateLinks,
                    StartObj = current_idx,
                    EndObj = current_idx + num_per_thread
                };
                if (j.EndObj >= SimulatedNodes.Count)
                {
                    j.EndObj = SimulatedNodes.Count - 1;
                }
                current_idx = j.EndObj + 1;
            }
            OpStarted = true;
            // wait until all threads are finished
            while (AssignedJobs.Exists(x => x.Done == false))
            {
                Thread.Sleep(1);
            }
            AssignedJobs.Clear();
            OpStarted = false;
        }

        private void WorkerThreadLoop()
        {
            while(true)
            {
                if(OpStarted == false)
                {
                    Thread.Sleep(1);
                    continue;
                }
                ThreadJob job = AssignedJobs.Find(x => x.ThreadId == Thread.CurrentThread.ManagedThreadId);
                switch(job.OP)
                {
                    case ThreadOp.UpdateLinks:
                        {
                            foreach(IXILink l in SimulatedLinks.Values.Skip(job.StartObj).Take(job.EndObj))
                            {
                                l.Update();
                            }
                            break;
                        }
                    case ThreadOp.UpdateNodes:
                        {
                            foreach(IXINode n in SimulatedNodes.Values.Skip(job.StartObj).Take(job.EndObj-job.StartObj))
                            {
                                n.Update();
                            }
                            break;
                        }
                    case ThreadOp.Terminate:
                        {
                            job.Done = true;
                            return;
                        }
                }
                job.Done = true;
                while(OpStarted == true)
                {
                    Thread.Sleep(1);
                }
            }
        }

    }
}
