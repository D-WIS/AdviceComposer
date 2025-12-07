using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.Service
{
    internal class Configuration
    {
        public TimeSpan LoopDuration { get; set; } = TimeSpan.FromSeconds(1.0);
        public string? OPCUAURL { get; set; } = "opc.tcp://localhost:48030";
        public TimeSpan ControllerObsolescence { get; set; } = TimeSpan.FromSeconds(5.0);
        public TimeSpan ProcedureObsolescence { get; set; } = TimeSpan.FromSeconds(5.0);
        public TimeSpan FaultDetectionIsolationAndRecoveryObsolescence { get; set; } = TimeSpan.FromSeconds(5.0);
        public TimeSpan SafeOperatingEnvelopeObsolescence { get; set; } = TimeSpan.FromSeconds(5.0);

    }
}
