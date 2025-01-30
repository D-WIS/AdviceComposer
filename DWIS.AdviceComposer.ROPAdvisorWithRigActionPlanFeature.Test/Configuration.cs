using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.ROPAdvisorWithRigActionPlanFeature.Test
{
    internal class Configuration
    {
        public TimeSpan LoopDuration { get; set; } = TimeSpan.FromSeconds(1.0);
        public string? OPCUAURL { get; set; } = "opc.tcp://localhost:48030";
        public double FlowrateAmplitude { get; set; } = 520.0 / 60000.0;
        public double FlowrateAverage { get; set; } = 1900.0 / 60000.0;
        public double FlowratePeriod { get; set; } = 56.0;

        public double RotationalSpeedAmplitude { get; set; } = 23.0 / 60.0;
        public double RotationalSpeedAverage { get; set; } = 160.0 / 60.0;
        public double RotationalSpeedPeriod { get; set; } = 85.0;

        public double ROPAmplitude { get; set; } = 4.0 / 3600.0;
        public double ROPAverage { get; set; } = 26.0 / 3600.0;
        public double ROPPeriod { get; set; } = 84.0;

        public double WOBAmplitude { get; set; } = 2600.0;
        public double WOBAverage { get; set; } = 16000.0;
        public double WOBPeriod { get; set; } = 45.0;

        public double TOBAmplitude { get; set; } = 1800.0;
        public double TOBAverage { get; set; } = 15000.0;
        public double TOBPeriod { get; set; } = 56.0;

        public double DPAmplitude { get; set; } = 4e5;
        public double DPAverage { get; set; } = 15e5;
        public double DPPeriod { get; set; } = 27.0;
    }
}
