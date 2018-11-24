using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    class IXILink
    {
        public ulong ID { get; private set; }
        public ulong From { get; private set; }
        public ulong To { get; private set; }

        public ulong LatencyTicks { get; private set; }
        public double Reliability { get; private set; }

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

        public void Update()
        {

        }
    }
}
