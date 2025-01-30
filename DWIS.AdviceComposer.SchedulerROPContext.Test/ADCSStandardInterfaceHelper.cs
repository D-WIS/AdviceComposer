using DWIS.Vocabulary.Schemas;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using System.Reflection;

namespace DWIS.AdviceComposer.SchedulerROPContext.Test
{
    [AccessToVariable(CommonProperty.VariableAccessType.Readable)]
    [SemanticTypeVariable("ADCSStandardFunction")]
    [SemanticFact("ADCSStandardFunction", Nouns.Enum.DrillingSignal)]
    [SemanticFact("ADCSStandardFunction#01", Nouns.Enum.ActivableFunction)]
    [SemanticFact("ADCSStandardFunction#01", Verbs.Enum.HasValue, "ADCSStandardFunction")]
    [SemanticFact("DWISADCSCapabilityDescriptor", Nouns.Enum.DWISADCSCapabilityDescriptor)]
    [SemanticFact("ADCSStandardFunction#01", Verbs.Enum.IsProvidedBy, "DWISADCSCapabilityDescriptor")]
    internal class ADCSStandardInterfaceHelper
    {

    }

}
