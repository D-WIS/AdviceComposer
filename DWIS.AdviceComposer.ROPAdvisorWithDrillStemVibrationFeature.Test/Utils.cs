using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.ROPAdvisorWithDrillStemVibrationFeature.Test
{
    internal class Utils
    {
        public static List<string>? CommonVariables(List<List<string>> vars)
        {
            if (vars != null && vars.Count > 0)
            {
                List<List<string>> variables = new List<List<string>>();
                for (int i = 1; i < vars.Count; i++)
                {
                    variables.Add(vars[i]);
                }
                return CommonVariables(vars[0], variables);
            }
            return null;
        }

        private static List<string>? CommonVariables(List<string> vs, List<List<string>> vars)
        {
            if (vars == null || vars.Count == 0) return vs;
            List<string> commonVariables = new List<string>();
            if (vars[0] != null)
            {
                foreach (var v in vs)
                {
                    if (vars[0].Contains(v))
                    {
                        commonVariables.Add(v);
                    }
                }
            }
            List<List<string>> variables = new List<List<string>>();
            for (int i = 1; i < vars.Count; i++)
            {
                variables.Add(vars[i]);
            }
            return CommonVariables(commonVariables, variables);
        }
    }
}
