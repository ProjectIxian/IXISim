using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    enum NodeState
    {
        Connecting,
        Synchronizing,
        Operating
    }
    class IXINode
    {
        public ulong ID { get; private set; }
        public double ProcessingSpeed { get; private set; }
        public bool FullHistory { get; private set; }

        //
        public ulong RedactedBottom { get; private set; }
        public ulong RedactedTop { get; private set; }
        public Dictionary<ulong, ulong> TransactionsPerBlock { get; private set; }
        public Dictionary<ulong, HashSet<ulong>> SignaturesPerBlock { get; private set; }

        // State == Synchronizing
        public SortedSet<ulong> BlocksToSync { get; private set; }
        public ulong WaitRetryTick { get; private set; }

        // State == Operating
        public ulong WorkingOnBlock { get; private set; }
        public HashSet<ulong> WorkingOnBlockSigs { get; private set; }
        public ulong WorkingOnBlockTXs { get; private set; }
        public ulong WorkingOnBlockProcessingUntil { get; private set; }

        public NodeState State { get; private set; }
        private Queue<(ulong, NetworkMessage)> IncomingMessages;

        public String GetDescription()
        {
            return String.Format("(@{0:0.0000} - FH:{1} - State:{2})",
                ProcessingSpeed,
                FullHistory ? "Y" : "N",
                State.ToString());
        }
        public String GetState(int ident)
        {
            String ws = new String(' ', ident);
            return String.Format("{0}State:{1}`n{0}Speed: {1:0.0000}`n{0}Full history:{3}`n{0}Blocks: {4} - {5}`n{0}Transactions in pool:{6}",
                ident, 
                State.ToString(),
                ProcessingSpeed, 
                FullHistory?"Yes":"No",
                RedactedBottom, RedactedTop,
                GetTotalTransactions());
        }

        public IXINode(bool full_history, double proc_speed = -1)
        {
            ID = SimulationController.Instance.GetNextID();
            FullHistory = full_history;
            State = NodeState.Connecting;
            if (proc_speed > 0)
            {
                ProcessingSpeed = proc_speed;
            }
            else
            {
                double range = (Config.NodeProcessingSpeedMax - Config.NodeProcessingSpeedMin);
                ProcessingSpeed = SimulationController.Instance.RNG.NextDouble() * range + Config.NodeProcessingSpeedMin;
            }
            ClearState();
        }

        public void IncomingMessage(ulong from, NetworkMessage msg)
        {
            IncomingMessages.Enqueue((from, msg));
        }

        public void Update()
        {
            switch (State)
            {
                case NodeState.Connecting:
                    {
                        UpdateState_Connecting();
                        break;
                    }
                case NodeState.Synchronizing:
                    {
                        UpdateState_Synchronizing();
                        break;
                    }
                case NodeState.Operating:
                    {
                        UpdateState_Operating();
                        break;
                    }
            }
        }

        public ulong GetTotalTransactions()
        {
            return TransactionsPerBlock.Values.Aggregate((ulong)0, (sum, next) => sum + next, sum => sum);
        }

        private void ClearState()
        {
            RedactedBottom = 0;
            RedactedTop = 0;
            TransactionsPerBlock = new Dictionary<ulong, ulong>();
            SignaturesPerBlock = new Dictionary<ulong, HashSet<ulong>>();
            BlocksToSync = new SortedSet<ulong>();
            IncomingMessages = new Queue<(ulong, NetworkMessage)>();
            WaitRetryTick = 0;
        }

        private void UpdateState_Connecting()
        {
            IXINode[] neighbors = SimulationController.Instance.GetAllNeighbors(ID);
            if (((ulong)neighbors.Count()) < Config.NodeLinksOutgoing)
            {
                ulong target_node = SimulationController.Instance.PickRandomNode();
                if (((ulong)SimulationController.Instance.GetIncomingNeighbors(target_node).Count()) < Config.NodeLinksIncomingMax)
                {
                    IXILink l = new IXILink(ID, target_node);
                    SimulationController.Instance.AddLink(l);
                    l.SendMessage(NetworkMessageType.Hello, 0);
                }
            }
            while(IncomingMessages.Count() > 0)
            {
                (ulong from, NetworkMessage msg) = IncomingMessages.Dequeue();
                if(msg.Type == NetworkMessageType.HelloReply)
                {
                    ClearState();
                    RedactedTop = msg.BlockData;
                    RedactedBottom = RedactedTop > Config.RedactedWindowSize ? RedactedTop - Config.RedactedWindowSize : 0;
                    for(ulong b = RedactedTop; b > RedactedBottom; b--)
                    {
                        BlocksToSync.Add(b);
                    }
                    ChangeState(NodeState.Synchronizing);
                }
            }
        }

        private void UpdateState_Synchronizing()
        {
            IXINode[] neighbors = SimulationController.Instance.GetAllNeighbors(ID);
            if (neighbors.Count() == 0)
            {
                SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                {
                    Type = EventType.NodeSyncFailed,
                    ObjectName = GetDescription(),
                    Message = "All neighbors lost."
                });
                ClearState();
                ChangeState(NodeState.Connecting);
                return;
            }
            else if (IncomingMessages.Count > 0)
            {
                while (IncomingMessages.Count > 0)
                {
                    (ulong from, NetworkMessage msg) = IncomingMessages.Dequeue();
                    ParseIncomingMessage_Synchronizing(from, msg);
                }
            }
            // internal update state
            if (BlocksToSync.Count() == 0 && TransactionsPerBlock.Count() == (int)(RedactedTop - RedactedBottom))
            {
                SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                {
                    Type = EventType.NodeSyncSuccess,
                    ObjectName = GetDescription(),
                    Message = "Synchronization completed."
                });
                ChangeState(NodeState.Operating);
            }
            else
            {
                // request blocks and transactions from neighbors
                if (WaitRetryTick <= SimulationController.Instance.CurrentTick)
                {
                    // request missing blocks
                    foreach (ulong b in BlocksToSync.Take((int)Config.MaxRequests))
                    {
                        ulong target = neighbors[SimulationController.Instance.RNG.Next(neighbors.Count())].ID;
                        SimulationController.Instance.GetLink(ID, target).SendMessage(NetworkMessageType.GetBlock, b);
                    }
                    // request missing transactions
                    ulong count = 0;
                    for (ulong i = RedactedBottom; i <= RedactedTop; i++)
                    {
                        if (BlocksToSync.Contains(i)) continue;
                        if (TransactionsPerBlock.ContainsKey(i)) continue;
                        ulong target = neighbors[SimulationController.Instance.RNG.Next(neighbors.Count())].ID;
                        SimulationController.Instance.GetLink(ID, target).SendMessage(NetworkMessageType.GetTransactions, i);
                        count++;
                        if (count >= Config.MaxRequests) break;
                    }
                    // sleep for a while
                    WaitRetryTick = SimulationController.Instance.CurrentTick + Config.RequestDelay;
                }
            }
        }

        private void UpdateState_Operating()
        {
            IXINode[] neighbors = SimulationController.Instance.GetAllNeighbors(ID);
            if (neighbors.Count() == 0)
            {
                SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                {
                    Type = EventType.NodeSyncFailed,
                    ObjectName = GetDescription(),
                    Message = "All neighbors lost."
                });
                ClearState();
                ChangeState(NodeState.Connecting);
                return;
            }
            else if (IncomingMessages.Count > 0)
            {
                while (IncomingMessages.Count > 0)
                {
                    (ulong from, NetworkMessage msg) = IncomingMessages.Dequeue();
                    ParseIncomingMessage_Operating(from, msg);
                }
            }
            // TODO: Node upkeep and generating new blocks (elect)
        }

        private void ParseIncomingMessage_Synchronizing(ulong from, NetworkMessage msg)
        {
            if(msg.Type == NetworkMessageType.HelloReply || msg.Type == NetworkMessageType.BlockData)
            {
                if (msg.BlockData > RedactedTop)
                {
                    SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                    {
                        Type = EventType.NodeProcessingEvent,
                        ObjectName = GetDescription(),
                        Message = String.Format("Sync target changes: {0} -> {1}",
                        RedactedTop, msg.BlockData)
                    });
                    for(ulong b = RedactedTop+1; b <= msg.BlockData;b++)
                    {
                        BlocksToSync.Add(b);
                    }
                    RedactedTop = msg.BlockData;
                }
                if(BlocksToSync.Remove(msg.BlockData))
                {
                    SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                    {
                        Type = EventType.NodeProcessingEvent,
                        ObjectName = GetDescription(),
                        Message = String.Format("Synchronized block {0}", msg.BlockData)
                    });
                }
            }
            if(msg.Type == NetworkMessageType.TransactionData)
            {
                TransactionsPerBlock.Add(msg.BlockData, msg.Amount);
                SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                {
                    Type = EventType.NodeProcessingEvent,
                    ObjectName = GetDescription(),
                    Message = String.Format("Synchronized transactions for block {0}: {1} TXs", msg.BlockData, msg.Amount)
                });
            }
        }

        private void ParseIncomingMessage_Operating(ulong from, NetworkMessage msg)
        {
            if (msg.Type == NetworkMessageType.BlockData)
            {
                if (WorkingOnBlock == 0)
                {
                    if (!TransactionsPerBlock.ContainsKey(msg.BlockData) && msg.BlockData == RedactedTop+1)
                    {
                        SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                        {
                            Type = EventType.NodeProcessingEvent,
                            ObjectName = GetDescription(),
                            Message = String.Format("Received new block {0}. Starting verification.", msg.BlockData)
                        });
                        WorkingOnBlock = msg.BlockData;
                        WorkingOnBlockTXs = msg.Amount;
                        WorkingOnBlockSigs = new HashSet<ulong>();
                    }
                }
                int sigs = WorkingOnBlockSigs.Count();
                WorkingOnBlockSigs.UnionWith(msg.Signers);
                if (sigs != WorkingOnBlockSigs.Count())
                {
                    ulong processing_time_ticks = SimulationController.Instance.SecondsToTicks(Config.BlockProcessingTimeMS);
                    WorkingOnBlockProcessingUntil = SimulationController.Instance.CurrentTick + (ulong)(processing_time_ticks * ProcessingSpeed);
                }
            }
            if(WorkingOnBlock > 0 && WorkingOnBlockProcessingUntil <= SimulationController.Instance.CurrentTick)
            {
                // processing finished
                if((ulong)WorkingOnBlockSigs.Count() >= SimulationController.Instance.NetworkConsensus)
                {
                    SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                    {
                        Type = EventType.NodeProcessingEvent,
                        ObjectName = GetDescription(),
                        Message = String.Format("Processing for block {0} finished. Transactions: {1}. Signatures: {2}",
                        WorkingOnBlock,
                        WorkingOnBlockTXs,
                        WorkingOnBlockSigs.Count())
                    });
                    AppendBlock();
                }
            }
            // TODO: Check for requests (get block, get transactions)
        }

        private void ChangeState(NodeState newstate)
        {
            SimulationController.Instance.Events.AddEvent(new SimulationEvent()
            {
                Type = EventType.NodeStateChange,
                ObjectName = GetDescription(),
                Message = String.Format("Changing state: {0} -> {1}",
                State.ToString(), newstate.ToString())
            });
            State = newstate;
        }

        private void AppendBlock()
        {
            RedactedTop = WorkingOnBlock;
            TransactionsPerBlock.Add(WorkingOnBlock, WorkingOnBlockTXs);
            SignaturesPerBlock.Add(WorkingOnBlock, WorkingOnBlockSigs);
            SimulationController.Instance.Events.AddEvent(new SimulationEvent()
            {
                Type = EventType.NodeAcceptedBlock,
                ObjectName = GetDescription(),
                Message = String.Format("Accepted block {0}", WorkingOnBlock)
            });
            WorkingOnBlock = 0;
            WorkingOnBlockTXs = 0;
            WorkingOnBlockSigs = null;
        }
    }
}
