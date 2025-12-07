using DWIS.RigOS.Capabilities.SOE.Model;
using System;

namespace DWIS.AdviceComposer.Service
{
    internal class SafeOperatingEnvelopeFunctionData
    {
        public SafeOperatingEnvelopeFunction? SafeOperatingEnvelopeFunction { get; set; } = null;
        public Guid ContextID { get; set; } = Guid.Empty;
        public Guid ParametersID { get; set; } = Guid.Empty;
        public Guid ParametersDestinationID { get; set; } = Guid.Empty;
    }
}
