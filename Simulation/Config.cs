using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IXISim.Simulation
{
    class Config
    {
        // Global controls
        public static double SimulationTickSeconds = 0.001; // Each tick is _ seconds

        // Node settings
        public static ulong Nodes = 10;
        public static ulong NodeLinksOutgoing = 4;
        public static ulong NodeLinksIncomingMax = 16;
        public static ulong BlockProcessingTimeMS = 10;
        public static double NodeProcessingSpeedMin = 0.2;
        public static double NodeProcessingSpeedMax = 2.0;
        public static ulong RedactedWindowSize = 40320; // 14 days

        // Sync settings
        public static ulong MaxRequests = 10;
        public static ulong RequestDelay = 100;

        // Link settings
        public static double LinkLatencyMSMin = 5;
        public static double LinkLatencyMSMax = 200;
        public static double LinkReliabilityMax = 1; // 1.0 = 100% reliability, 0 = 0% reliability
        public static double LinkReliabilityMin = 0.95;
    }
}
