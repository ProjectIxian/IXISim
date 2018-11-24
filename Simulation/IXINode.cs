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

        public ulong RedactedBottom { get; private set; }
        public ulong RedactedTop { get; private set; }

        public ulong TXPoolSize { get; private set; }

        public NodeState State { get; private set; }

        public String GetDescription()
        {
            return String.Format("(@{0:0.0000} - FH:{1})",
                ProcessingSpeed,
                FullHistory ? "Y" : "N");
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
                TXPoolSize);
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
            RedactedBottom = 0;
            RedactedTop = 0;
            TXPoolSize = 0;
        }

        public void Update()
        {
            // TODO
        }
    }
}
