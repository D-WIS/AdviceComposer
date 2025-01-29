using DWIS.API.DTO;
using DWIS.Vocabulary.Schemas;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using System.Reflection;

namespace DWIS.AdviceComposer.Model
{
    [AccessToVariable(CommonProperty.VariableAccessType.Readable)]
    [SemanticTypeVariable("ADCSStandardFunction")]
    [SemanticFact("ADCSStandardFunction", Nouns.Enum.DrillingSignal)]
    [SemanticFact("ADCSStandardFunction#01", Nouns.Enum.ActivableFunction)]
    [SemanticFact("ADCSStandardFunction#01", Verbs.Enum.HasValue, "ADCSStandardFunction")]
    [SemanticFact("DWISADCSCapabilityDescriptor", Nouns.Enum.DWISADCSCapabilityDescriptor)]
    [SemanticFact("ADCSStandardFunction#01", Verbs.Enum.IsProvidedBy, "DWISADCSCapabilityDescriptor")]
    public class ADCSStandardInterfaceHelper
    {
        public string? SparQLQuery { get; set; } = null;
        public List<string>? SparQLVariables { get; set; } = null;

        public ADCSStandardInterfaceHelper()
        {
            Assembly? assembly = Assembly.GetAssembly(typeof(ADCSStandardInterfaceHelper));
            var queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardInterfaceHelper).FullName);
            if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0)
            {
                SparQLQuery = queries.First().Value.SparQL;
                SparQLVariables = queries.First().Value.Variables;
            }
        }
    }
}
