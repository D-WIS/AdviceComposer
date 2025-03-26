using DWIS.API.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.Model
{
    public class ProcedureData
    {
        public List<Vocabulary.Schemas.Nouns.Enum> Features { get; set; } = new List<Vocabulary.Schemas.Nouns.Enum>();
        public string AdvisorName { get; set; } = string.Empty;
        public object? Parameters { get; set; } = null;
        public QueryResult? ParametersDestinationQueryResult { get; set; } = null;
    }

}
