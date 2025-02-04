using DWIS.RigOS.Capabilities.Controller.Model;
using DWIS.RigOS.Common.Model;
using OSDC.DotnetLibraries.General.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.Model
{
    public class ControllerFunctionDescription
    {
        public ControllerFunction? ControllerFunction { get; set; } = null;

        public Dictionary<Guid, TokenizedAssignableReference<ScalarValue>> SetPoints { get; set; } = new Dictionary<Guid, TokenizedAssignableReference<ScalarValue>>();
        public Dictionary<Guid, TokenizedAssignableReference<ScalarValue>> Limits { get; set; } = new Dictionary<Guid, TokenizedAssignableReference<ScalarValue>>();
    }
}
