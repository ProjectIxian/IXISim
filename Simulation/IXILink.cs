using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    enum NetworkMessageType
    {
        Hello,
        HelloReply,
        GetBlock,
        BlockData,
        GetTransactions,
        TransactionData
    }

    struct NetworkMessage
    {
        public NetworkMessageType Type;
        public ulong BlockData;
        public ulong ArrivesOnTick;
    }

    class IXILink
    {
        public ulong ID { get; private set; }
        public ulong From { get; private set; }
        public ulong To { get; private set; }

        public ulong LatencyTicks { get; private set; }
        public double Reliability { get; private set; }

        private Queue<NetworkMessage> Messages;
        private bool Disconnected;

        public String GetDescription()
        {
            return String.Format("({0} -> {1}, Latency: {2} ({3:0.0000} s), Reliability: {4:0.0000})", 
                From, To, 
                LatencyTicks, SimulationController.Instance.TicksToSeconds(LatencyTicks), 
                Reliability);
        }

        public IXILink(ulong from, ulong to, double reliability = -1, ulong latency = 0)
        {
            ID = SimulationController.Instance.GetNextID();
            Disconnected = false;
            Messages = new Queue<NetworkMessage>();
            From = from;
            To = to;
            if(reliability > 0)
            {
                Reliability = reliability;
            } else
            {
                double range = Config.LinkReliabilityMax - Config.LinkReliabilityMin;
                Reliability = SimulationController.Instance.RNG.NextDouble() * range + Config.LinkReliabilityMin;
            }
            if(Reliability < 0)
            {
                Reliability = 0.0;
            }
            if(Reliability > 1)
            {
                Reliability = 1;
            }
            if(latency > 0)
            {
                LatencyTicks = latency;
            } else
            {
                double range = Config.LinkLatencyMSMax - Config.LinkLatencyMSMin;
                double latency_ms = SimulationController.Instance.RNG.NextDouble() * range + Config.LinkLatencyMSMin;
                LatencyTicks = SimulationController.Instance.SecondsToTicks(latency_ms / 1000);
            }
        }

        public void Disconnect()
        {
            Disconnected = true;
        }

        public void SendMessage(NetworkMessageType type, ulong block_data = 0)
        {
            // check reliability
            if(SimulationController.Instance.RNG.NextDouble() > Reliability)
            {
                SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                {
                    Type = EventType.NetworkMessageLost,
                    ObjectName = String.Format("Message({0} -> {1}; Link {2})", From, To, ID),
                    Message = "Network message was lost during transit."
                });
            } else
            {
                Messages.Enqueue(new NetworkMessage()
                {
                    Type = type,
                    BlockData = block_data,
                    ArrivesOnTick = SimulationController.Instance.CurrentTick + LatencyTicks
                });
            }
        }

        public void Update()
        {
            CheckDisconnect();
            if (Disconnected)
            {
                SimulationController.Instance.DropLink(ID);
            }
            else
            {
                Transmit();
            }
        }

        private void CheckDisconnect()
        {
            // if either end of the connection is gone
            if(!SimulationController.Instance.NodeExists(From) || !SimulationController.Instance.NodeExists(To))
            {
                Disconnected = true;
            }
        }

        private void Transmit()
        {
            while(Messages.Count > 0 && Messages.Peek().ArrivesOnTick <= SimulationController.Instance.CurrentTick)
            {
                NetworkMessage msg = Messages.Dequeue();
                SimulationController.Instance.Events.AddEvent(new SimulationEvent()
                {
                    Type = EventType.NetworkMessageTransmitted,
                    ObjectName = String.Format("Message({0} -> {1}; Link {2})", From, To, ID),
                    Message = String.Format("Message: {0}, BlockData: {1}", msg.Type.ToString(), msg.BlockData)
                });
                SimulationController.Instance.GetNode(To).IncomingMessage(From, msg);
            }
        }
    }
}
