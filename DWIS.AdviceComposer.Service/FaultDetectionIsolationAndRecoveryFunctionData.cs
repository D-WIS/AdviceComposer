using DWIS.RigOS.Capabilities.FDIR.Model;
using System;

namespace DWIS.AdviceComposer.Service
{
    internal class FaultDetectionIsolationAndRecoveryFunctionData
    {
        public FaultDetectionIsolationAndRecoveryFunction? FaultDetectionIsolationAndRecoveryFunction { get; set; } = null;
        public Guid ContextID { get; set; } = Guid.Empty;
        public Guid ParametersID { get; set; } = Guid.Empty;
        public Guid ParametersDestinationID { get; set; } = Guid.Empty;
    }
}
