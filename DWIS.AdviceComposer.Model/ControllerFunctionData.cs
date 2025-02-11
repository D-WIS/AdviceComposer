using DWIS.API.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DWIS.AdviceComposer.Model
{
    public class ControllerFunctionData
    {
        public List<Vocabulary.Schemas.Nouns.Enum> Features { get; set; } = new List<Vocabulary.Schemas.Nouns.Enum>();
        public string AdvisorName { get; set; } = string.Empty;
        public List<ControllerData> ControllerDatas { get; set; } = new List<ControllerData>();
    }

    public class ControllerData
    {
        public double? SetPointRecommendation { get; set; } = null;
        public double? SetPointRateOfChange { get; set; } = null;
        public double? MeasuredValue { get; set; } = null;
        public QueryResult? SetPointDestinationQueryResult { get; set; } = null;

        public List<ControllerLimitData> ControllerLimitDatas { get; set; } = new List<ControllerLimitData>();
    }

    public class ControllerLimitData
    {
        public double? LimitRecommendation { get; set; } = null;
        public double? LimitRateOfChange { get; set; } = null;
        public QueryResult? LimitDestinationQueryResult { get; set; } = null;
        public bool IsMin { get; set; } = false;
    }
}
