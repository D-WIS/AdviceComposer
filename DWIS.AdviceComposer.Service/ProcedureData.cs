using DWIS.RigOS.Capabilities.Controller.Model;
using DWIS.RigOS.Capabilities.Procedure.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.Service
{
    internal class ProcedureFunctionData
    {
        public ProcedureFunction? ProcedureFunction { get; set; } = null;
        public Guid ContextID { get; set; } = Guid.Empty;
        public Guid ParametersID { get; set; } = Guid.Empty;
        public Guid ParametersDestinationID { get; set; } = Guid.Empty;
    }
}
