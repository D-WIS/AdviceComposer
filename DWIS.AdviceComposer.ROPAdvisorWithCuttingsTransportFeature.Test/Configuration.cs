using Org.BouncyCastle.Asn1.Mozilla;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test
{
    internal class Configuration
    {
        public TimeSpan LoopDuration { get; set; } = TimeSpan.FromSeconds(1.0);
        public string? OPCUAURL { get; set; } = "opc.tcp://localhost:48030";
        public double FlowrateAmplitude { get; set; } = 500.0 / 60000.0;
        public double FlowrateAverage { get; set; } = 2000.0 / 60000.0;
        public double FlowratePeriod { get; set; } = 60.0;

        public double RotationalSpeedAmplitude { get; set; } = 30.0 / 60.0;
        public double RotationalSpeedAverage { get; set; } = 120.0 / 60.0;
        public double RotationalSpeedPeriod { get; set; } = 100.0;

        public double ROPAmplitude { get; set; } = 10.0 / 3600.0;
        public double ROPAverage { get; set; } = 30.0 / 3600.0;
        public double ROPPeriod { get; set; } = 45.0;

        public double WOBAmplitude { get; set; } = 5000.0;
        public double WOBAverage { get; set; } = 30000.0;
        public double WOBPeriod { get; set; } = 66.0;

        public double TOBAmplitude { get; set; } = 1000.0;
        public double TOBAverage { get; set; } = 20000.0;
        public double TOBPeriod { get; set; } = 22.0;

        public double DPAmplitude { get; set; } = 5e5;
        public double DPAverage { get; set; } = 20e5;
        public double DPPeriod { get; set; } = 52.0;
    }
}
