using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.SchedulerROPContext.Test
{
    internal class Configuration
    {
        public TimeSpan LoopDuration { get; set; } = TimeSpan.FromSeconds(1.0);
        public string? OPCUAURL { get; set; } = "opc.tcp://localhost:48030";

        public TimeSpan ContextChangePeriod { get; set; } = TimeSpan.FromSeconds(30.0);
    }
}
