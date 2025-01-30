using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test
{
    internal class Configuration
    {
        public TimeSpan LoopDuration { get; set; } = TimeSpan.FromSeconds(1.0);
        public string? OPCUAURL { get; set; } = "opc.tcp://localhost:48030";
        public double FlowrateAmplitude { get; set; } = 400.0 / 60000.0;
        public double FlowrateAverage { get; set; } = 2200.0 / 60000.0;
        public double FlowratePeriod { get; set; } = 42.0;

        public double RotationalSpeedAmplitude { get; set; } = 20.0 / 60.0;
        public double RotationalSpeedAverage { get; set; } = 150.0 / 60.0;
        public double RotationalSpeedPeriod { get; set; } = 95.0;

        public double ROPAmplitude { get; set; } = 7.0 / 3600.0;
        public double ROPAverage { get; set; } = 25.0 / 3600.0;
        public double ROPPeriod { get; set; } = 58.0;

        public double WOBAmplitude { get; set; } = 3500.0;
        public double WOBAverage { get; set; } = 26000.0;
        public double WOBPeriod { get; set; } = 74.0;

        public double TOBAmplitude { get; set; } = 1500.0;
        public double TOBAverage { get; set; } = 18000.0;
        public double TOBPeriod { get; set; } = 34.0;

        public double DPAmplitude { get; set; } = 7e5;
        public double DPAverage { get; set; } = 17e5;
        public double DPPeriod { get; set; } = 56.0;
    }
}
