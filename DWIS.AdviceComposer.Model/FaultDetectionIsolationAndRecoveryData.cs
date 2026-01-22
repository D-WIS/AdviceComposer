using DWIS.API.DTO;
using System.Collections.Generic;

namespace DWIS.AdviceComposer.Model
{
    public class FaultDetectionIsolationAndRecoveryData
    {
        public List<Vocabulary.Schemas.Nouns.Enum> Features { get; set; } = new List<Vocabulary.Schemas.Nouns.Enum>();
        public string AdvisorName { get; set; } = string.Empty;
        public object? Parameters { get; set; } = null;
        public QueryResult? ParametersDestinationQueryResult { get; set; } = null;
    }
}
