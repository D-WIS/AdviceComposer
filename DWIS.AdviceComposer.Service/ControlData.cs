using DWIS.RigOS.Capabilities.Controller.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.Service
{
    internal class ControlFunctionData
    {
        public ControllerFunction? ControllerFunction { get; set; } = null;
        public Guid ContextID { get; set; } = Guid.Empty;
        public Guid ParametersID { get; set; } = Guid.Empty;
        public Guid ParametersDestinationID { get; set; } = Guid.Empty;
        public List<ControlData> controllerDatas { get; set; } = new List<ControlData>();
    }
    internal class ControlData
    {
        public Guid ParametersID { get; set; } = Guid.Empty;
        public Guid ParametersDestinationID { get; set; } = Guid.Empty;
        public Guid SetPointSourceID { get; set; } = Guid.Empty;
        public Guid SetPointDestinationID { get; set; } = Guid.Empty;
        public Guid MeasuredValueID { get; set; } = Guid.Empty;
        public Guid MaxRateOfChangeID { get; set; } = Guid.Empty;
        public List<LimitData> LimitIDs { get; set; } = new List<LimitData>();
    }

    internal class LimitData
    {
        public Guid MaxLimitSourceID { get; set; } = Guid.Empty;
        public Guid MaxLimitDestinationID { get; set; } = Guid.Empty;
        public Guid MeasuredValueID { get; set; } = Guid.Empty;
        public Guid MaxRateOfChangeID { get; set; } = Guid.Empty;
        public bool IsMin { get; set; } = false;
    }
}
