using DWIS.API.DTO;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.Client.ReferenceImplementation;
using DWIS.RigOS.Capabilities.Controller.Model;
using DWIS.RigOS.Common.Model;
using Newtonsoft.Json;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using System.Reflection;

namespace DWIS.AdviceComposer.ROPAdvisorWithCuttingsTransportFeature.Test
{
    public class Worker : BackgroundService
    {
        private ILogger<DWISClientOPCF>? _loggerDWISClient;
        private ILogger<Worker>? _logger;
        private IOPCUADWISClient? _DWISClient = null;
        private TimeSpan _loopSpan;

        private Configuration Configuration { get; set; } = new Configuration();

        private QueryResult? _flowrateSetPointPlaceHolder = null;
        private QueryResult? _rotationalSpeedSetPointPlaceHolder  = null;
        private QueryResult? _ROPMaxLimitPlaceHolder  = null;
        private QueryResult? _WOBMaxLimitPlaceHolder = null;
        private QueryResult? _TOBMaxLimitPlaceHolder = null;
        private QueryResult? _DPMaxLimitPlaceHolder = null;

        private Dictionary<string, (string sparql, string key)> registeredQueriesSparqls_ = new Dictionary<string, (string sparql, string key)>();

        private List<AcquiredSignals> placeHolders_ = new List<AcquiredSignals>();

        private static string _ADCSStandardInterfaceSubscriptionName = "ADCSStandardInterfaceSubscription";
        private static string _manifestName = "ROPAdvisorWithCuttingsTransportFeature";
        private static string _prefix = "DWIS:Advisor:Sekal:ROPManagement:";
        private static string _companyName = "Sekal";

        public Worker(ILogger<Worker>? logger, ILogger<DWISClientOPCF>? loggerDWISClient)
        {
            _logger = logger;
            _loggerDWISClient = loggerDWISClient;
            Initialize();
        }

        private void Initialize()
        {
            string homeDirectory = ".." + Path.DirectorySeparatorChar + "home";
            if (!Directory.Exists(homeDirectory))
            {
                try
                {
                    Directory.CreateDirectory(homeDirectory);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Impossible to create home directory for local storage");
                }
            }
            if (Directory.Exists(homeDirectory))
            {
                string configName = homeDirectory + Path.DirectorySeparatorChar + "config.json";
                if (File.Exists(configName))
                {
                    string jsonContent = File.ReadAllText(configName);
                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        try
                        {
                            Configuration? config = System.Text.Json.JsonSerializer.Deserialize<Configuration>(jsonContent);
                            if (config != null)
                            {
                                Configuration = config;
                            }
                        }
                        catch (Exception e)
                        {
                            if (_logger != null)
                            {
                                _logger.LogError(e.ToString());
                            }
                        }
                    }
                }
                else
                {
                    string defaultConfigJson = System.Text.Json.JsonSerializer.Serialize(Configuration);
                    using (StreamWriter writer = new StreamWriter(configName))
                    {
                        writer.WriteLine(defaultConfigJson);
                    }
                }
            }
            if (_logger != null)
            {
                _logger.LogInformation("Configuration Loop Duration: " + Configuration.LoopDuration.ToString());
                _logger.LogInformation("Configuration OPCUAURAL: " + Configuration.OPCUAURL);
            }
            string hostName = System.Net.Dns.GetHostName();
            if (!string.IsNullOrEmpty(hostName))
            {
                var ip = System.Net.Dns.GetHostEntry(hostName);
                if (ip != null && ip.AddressList != null && ip.AddressList.Length > 0 && _logger != null)
                {
                    _logger.LogInformation("My IP Address: " + ip.AddressList[0].ToString());
                }
            }
        }

        private void ConnectToBlackboard()
        {
            try
            {
                if (Configuration != null && !string.IsNullOrEmpty(Configuration.OPCUAURL))
                {
                    DefaultDWISClientConfiguration defaultDWISClientConfiguration = new DefaultDWISClientConfiguration();
                    defaultDWISClientConfiguration.UseWebAPI = false;
                    defaultDWISClientConfiguration.ServerAddress = Configuration.OPCUAURL;
                    _loopSpan = Configuration.LoopDuration;
                    _DWISClient = new DWISClientOPCF(defaultDWISClientConfiguration, _loggerDWISClient);
                }
            }
            catch (Exception e)
            {
                if (_logger != null)
                {
                    _logger.LogError(e.ToString());
                }
            }
        }

        private bool RegisterToBlackboard(TokenizedAssignableReferenceScalarValue? assignable, IOPCUADWISClient? DWISClient, ref QueryResult? placeHolder)
        {
            bool ok = false;
            if (DWISClient != null && assignable != null && assignable.Manifest != null && !string.IsNullOrEmpty(assignable.SparQLQuery) && assignable.SparQLVariables != null && assignable.SparQLVariables.Count > 0)
            {
                ManifestFile manifestFile = assignable.Manifest;
                string sparQLQuery = assignable.SparQLQuery;
                List<string>? variables = assignable.SparQLVariables.ToList<string>();
                QueryResult? res = null;
                var result = DWISClient.GetQueryResult(sparQLQuery);
                if (result != null && result.Results != null && result.Results.Count > 0)
                {
                    res = result;
                }
                // if we couldn't find any answer then the manifest must be injected
                if (res == null)
                {
                    if (manifestFile != null)
                    {
                        string json = System.Text.Json.JsonSerializer.Serialize(manifestFile);
                        if (!string.IsNullOrEmpty(json))
                        {
                            DWIS.API.DTO.ManifestFile? manifest = System.Text.Json.JsonSerializer.Deserialize<DWIS.API.DTO.ManifestFile>(json);
                            if (manifest != null)
                            {
                                ManifestInjectionResult? r = DWISClient.Inject(manifest);
                                if (r != null && r.Success)
                                {
                                    if (r.ProvidedVariables != null && r.ProvidedVariables.Count > 0)
                                    {
                                        placeHolder = new QueryResult();
                                        QueryResultRow row = new QueryResultRow();
                                        List<DWIS.API.DTO.NodeIdentifier> items = new List<DWIS.API.DTO.NodeIdentifier>();
                                        placeHolder.VariablesHeader = variables;
                                        row.Items = items;
                                        foreach (var kvp in r.ProvidedVariables)
                                        {
                                            DWISClient.GetNameSpace(kvp.InjectedID.NameSpaceIndex, out string ns);
                                            items.Add(new DWIS.API.DTO.NodeIdentifier() { ID = kvp.InjectedID.ID, NameSpace = ns });
                                        }
                                        placeHolder.Add(row);
                                        ok = true;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // a manifest has already been injected.
                    placeHolder = res;
                    ok = true;
                }
            }
            return ok;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ConnectToBlackboard();
            if (_DWISClient != null && _DWISClient.Connected)
            {
                Assembly? assembly = Assembly.GetAssembly(typeof(ADCSStandardAutoDriller));
                var queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDriller).FullName);
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0)
                {
                    string? sparql = queries.First().Value.SparQL;
                    if (!string.IsNullOrEmpty(sparql))
                    {
                        var result = _DWISClient.RegisterQuery(sparql, CapabilityDescriptionCallBack);
                        if (!string.IsNullOrEmpty(result.jsonQueryDiff))
                        {
                            var queryDiff = QueryResultsDiff.FromJsonString(result.jsonQueryDiff);
                            if (queryDiff != null && queryDiff.Added != null && queryDiff.Added.Any())
                            {
                                registeredQueriesSparqls_.Add(queryDiff.QueryID, (sparql, _ADCSStandardInterfaceSubscriptionName));
                                placeHolders_.Add(AcquiredSignals.CreateWithSubscription(new string[] { sparql }, new string[] { _ADCSStandardInterfaceSubscriptionName }, 0, _DWISClient));
                            }
                        }
                    }
                }
                ADCSStandardAutoDrillerCuttingsTransportFeatureHelper helper = new ADCSStandardAutoDrillerCuttingsTransportFeatureHelper();
                assembly = Assembly.GetAssembly(typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper));
                queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "BOSFlowrateSetPoint");
                ManifestFile? manifest = GeneratorSparQLManifestFile.GetManifestFile(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "BOSFlowrateSetPoint", _manifestName, _companyName, _prefix + "BOSFlowrateSetPoint");
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0 && manifest != null)
                {
                    helper.BOSFlowrateSetPoint.SparQLQuery = queries.First().Value.SparQL;
                    helper.BOSFlowrateSetPoint.SparQLVariables = queries.First().Value.Variables;
                    helper.BOSFlowrateSetPoint.Manifest = manifest;
                    RegisterToBlackboard(helper.BOSFlowrateSetPoint, _DWISClient, ref _flowrateSetPointPlaceHolder);
                }
                queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "BOSAngularVelocitySetPoint");
                manifest = GeneratorSparQLManifestFile.GetManifestFile(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "BOSAngularVelocitySetPoint", _manifestName, _companyName, _prefix + "BOSAngularVelocitySetPoint");
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0 && manifest != null)
                {
                    helper.BOSAngularVelocitySetPoint.SparQLQuery = queries.First().Value.SparQL;
                    helper.BOSAngularVelocitySetPoint.SparQLVariables = queries.First().Value.Variables;
                    helper.BOSAngularVelocitySetPoint.Manifest = manifest;
                    RegisterToBlackboard(helper.BOSAngularVelocitySetPoint, _DWISClient, ref _rotationalSpeedSetPointPlaceHolder);
                }
                queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "ROPMaxLimitReference");
                manifest = GeneratorSparQLManifestFile.GetManifestFile(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "ROPMaxLimitReference", _manifestName, _companyName, _prefix + "ROPMaxLimitReference");
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0 && manifest != null)
                {
                    helper.ROPMaxLimitReference.SparQLQuery = queries.First().Value.SparQL;
                    helper.ROPMaxLimitReference.SparQLVariables = queries.First().Value.Variables;
                    helper.ROPMaxLimitReference.Manifest = manifest;
                    RegisterToBlackboard(helper.ROPMaxLimitReference, _DWISClient, ref _ROPMaxLimitPlaceHolder);
                }
                queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "WOBMaxLimitReference");
                manifest = GeneratorSparQLManifestFile.GetManifestFile(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "WOBMaxLimitReference", _manifestName, _companyName, _prefix + "WOBMaxLimitReference");
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0 && manifest != null)
                {
                    helper.WOBMaxLimitReference.SparQLQuery = queries.First().Value.SparQL;
                    helper.WOBMaxLimitReference.SparQLVariables = queries.First().Value.Variables;
                    helper.WOBMaxLimitReference.Manifest = manifest;
                    RegisterToBlackboard(helper.WOBMaxLimitReference, _DWISClient, ref _WOBMaxLimitPlaceHolder);
                }
                queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "BitTorqueMaxLimitReference");
                manifest = GeneratorSparQLManifestFile.GetManifestFile(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "BitTorqueMaxLimitReference", _manifestName, _companyName, _prefix + "BitTorqueMaxLimitReference");
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0 && manifest != null)
                {
                    helper.BitTorqueMaxLimitReference.SparQLQuery = queries.First().Value.SparQL;
                    helper.BitTorqueMaxLimitReference.SparQLVariables = queries.First().Value.Variables;
                    helper.BitTorqueMaxLimitReference.Manifest = manifest;
                    RegisterToBlackboard(helper.BitTorqueMaxLimitReference, _DWISClient, ref _TOBMaxLimitPlaceHolder);
                }
                queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "DifferentialPressureMaxLimitReference");
                manifest = GeneratorSparQLManifestFile.GetManifestFile(assembly, typeof(ADCSStandardAutoDrillerCuttingsTransportFeatureHelper).FullName, "DifferentialPressureMaxLimitReference", _manifestName, _companyName, _prefix + "DifferentialPressureMaxLimitReference");
                if (queries != null && queries.Count > 0 && queries.First().Value != null && !string.IsNullOrEmpty(queries.First().Value.SparQL) && queries.First().Value.Variables != null && queries.First().Value.Variables!.Count > 0 && manifest != null)
                {
                    helper.DifferentialPressureMaxLimitReference.SparQLQuery = queries.First().Value.SparQL;
                    helper.DifferentialPressureMaxLimitReference.SparQLVariables = queries.First().Value.Variables;
                    helper.DifferentialPressureMaxLimitReference.Manifest = manifest;
                    RegisterToBlackboard(helper.DifferentialPressureMaxLimitReference, _DWISClient, ref _TOBMaxLimitPlaceHolder);
                }
                await Loop(cancellationToken);
            }
        }

        private void CapabilityDescriptionCallBack(QueryResultsDiff resultsDiff)
        {
            _logger?.LogInformation("Callback for capability descriptions");
            if (_DWISClient != null && _DWISClient.Connected && resultsDiff != null && resultsDiff.Added != null && resultsDiff.Added.Any())
            {
                if (registeredQueriesSparqls_.ContainsKey(resultsDiff.QueryID))
                {
                    var pair = registeredQueriesSparqls_[resultsDiff.QueryID];
                    var ac = AcquiredSignals.CreateWithSubscription(new string[] { pair.sparql }, new string[] { pair.key }, 0, _DWISClient);

                    var existing = placeHolders_.FirstOrDefault(ph => ph.Any() && ph.First().Key == pair.key);
                    if (existing != null)
                    {
                        int idx = placeHolders_.IndexOf(existing);
                        placeHolders_[idx] = ac;
                    }
                    else
                    {
                        placeHolders_.Add(ac);
                    }
                }
            }
        }

        private async Task Loop(CancellationToken cancellationToken)
        {
            ADCSStandardAutoDriller? autodriller = null;
            Dictionary<string, QueryResult> placeHolders = new Dictionary<string, QueryResult>();
            PeriodicTimer timer = new PeriodicTimer(_loopSpan);
            double Q = Configuration.FlowrateAverage;
            double omega = Configuration.RotationalSpeedAverage;
            double ROP = Configuration.ROPAverage;
            double WOB = Configuration.WOBAverage;
            double TOB = Configuration.TOBAmplitude;
            double DP = Configuration.DPAverage;
            DateTime start = DateTime.UtcNow;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (placeHolders_ != null)
                {
                    var existing = placeHolders_.FirstOrDefault(ph => ph.Any() && ph.First().Key == _ADCSStandardInterfaceSubscriptionName);
                    if (existing != null && existing.TryGetValue(_ADCSStandardInterfaceSubscriptionName, out List<AcquiredSignal>? signals))
                    {
                        if (signals != null && signals.Count > 0 && signals[0] != null)
                        {

                            string? json = signals[0].GetValue<string>();
                            if (!string.IsNullOrEmpty(json))
                            {
                                try
                                {
                                    var settings = new JsonSerializerSettings
                                    {
                                        TypeNameHandling = TypeNameHandling.Objects,
                                        Formatting = Formatting.Indented
                                    };
                                    autodriller = Newtonsoft.Json.JsonConvert.DeserializeObject<ADCSStandardAutoDriller>(json, settings);
                                    if (autodriller != null)
                                    {
                                       // read any information that may be useful for the advisor about the ADCS actual capabilities
                                    }
                                }
                                catch (Exception e)
                                {
                                    _logger?.LogError(e.ToString());
                                }
                            }
                        }
                    }
                    TimeSpan elapsed = start - DateTime.UtcNow;
                    Q = Configuration.FlowrateAverage + Configuration.FlowrateAmplitude * Math.Sin(2.0 * Math.PI * elapsed.TotalSeconds / Configuration.FlowratePeriod);
                    omega = Configuration.RotationalSpeedAverage + Configuration.RotationalSpeedAmplitude * Math.Sin(2.0 * Math.PI * elapsed.TotalSeconds / Configuration.RotationalSpeedPeriod);
                    ROP = Configuration.ROPAverage + Configuration.ROPAmplitude * Math.Sin(2.0 * Math.PI * elapsed.TotalSeconds / Configuration.ROPPeriod);
                    WOB = Configuration.WOBAverage + Configuration.WOBAmplitude * Math.Sin(2.0 * Math.PI * elapsed.TotalSeconds / Configuration.WOBPeriod);
                    TOB  = Configuration.TOBAverage + Configuration.TOBAmplitude * Math.Sin(2.0 * Math.PI * elapsed.TotalSeconds / Configuration.TOBPeriod);
                    DP = Configuration.DPAverage + Configuration.DPAmplitude * Math.Sin(2.0 * Math.PI * elapsed.TotalSeconds / Configuration.DPPeriod);
                    if (_DWISClient != null && _flowrateSetPointPlaceHolder != null && _flowrateSetPointPlaceHolder.Count > 0 && _flowrateSetPointPlaceHolder[0].Count > 0)
                    {
                        NodeIdentifier id = _flowrateSetPointPlaceHolder[0][0];
                        if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                        {
                            // OPC-UA code to set the value at the node id = ID
                            (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                            outputs[0].nameSpace = id.NameSpace;
                            outputs[0].id = id.ID;
                            outputs[0].value = Q;
                            outputs[0].sourceTimestamp = DateTime.UtcNow;
                            bool ok =_DWISClient.UpdateAnyVariables(outputs);
                            if (ok)
                            {
                                _logger?.LogInformation("flowrate: " + (Q * 60000.0).ToString("F1"));
                            }
                        }
                    }
                    if (_DWISClient != null && _rotationalSpeedSetPointPlaceHolder != null && _rotationalSpeedSetPointPlaceHolder.Count > 0 && _rotationalSpeedSetPointPlaceHolder[0].Count > 0)
                    {
                        NodeIdentifier id = _rotationalSpeedSetPointPlaceHolder[0][0];
                        if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                        {
                            // OPC-UA code to set the value at the node id = ID
                            (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                            outputs[0].nameSpace = id.NameSpace;
                            outputs[0].id = id.ID;
                            outputs[0].value = omega;
                            outputs[0].sourceTimestamp = DateTime.UtcNow;
                            bool ok = _DWISClient.UpdateAnyVariables(outputs);
                            if (ok)
                            {
                                _logger?.LogInformation("rotational speed: " + (omega * 60.0).ToString("F1"));
                            }
                        }
                    }
                    if (_DWISClient != null && _ROPMaxLimitPlaceHolder != null && _ROPMaxLimitPlaceHolder.Count > 0 && _ROPMaxLimitPlaceHolder[0].Count > 0)
                    {
                        NodeIdentifier id = _ROPMaxLimitPlaceHolder[0][0];
                        if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                        {
                            // OPC-UA code to set the value at the node id = ID
                            (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                            outputs[0].nameSpace = id.NameSpace;
                            outputs[0].id = id.ID;
                            outputs[0].value = ROP;
                            outputs[0].sourceTimestamp = DateTime.UtcNow;
                            bool ok = _DWISClient.UpdateAnyVariables(outputs);
                            if (ok)
                            {
                                _logger?.LogInformation("ROP: " + (ROP * 3600.0).ToString("F1"));
                            }
                        }
                    }
                    if (_DWISClient != null && _WOBMaxLimitPlaceHolder != null && _WOBMaxLimitPlaceHolder.Count > 0 && _WOBMaxLimitPlaceHolder[0].Count > 0)
                    {
                        NodeIdentifier id = _WOBMaxLimitPlaceHolder[0][0];
                        if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                        {
                            // OPC-UA code to set the value at the node id = ID
                            (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                            outputs[0].nameSpace = id.NameSpace;
                            outputs[0].id = id.ID;
                            outputs[0].value = WOB;
                            outputs[0].sourceTimestamp = DateTime.UtcNow;
                            bool ok = _DWISClient.UpdateAnyVariables(outputs);
                            if (ok)
                            {
                                _logger?.LogInformation("WOB: " + (WOB / 1000.0).ToString("F1"));
                            }
                        }
                    }
                    if (_DWISClient != null && _TOBMaxLimitPlaceHolder != null && _TOBMaxLimitPlaceHolder.Count > 0 && _TOBMaxLimitPlaceHolder[0].Count > 0)
                    {
                        NodeIdentifier id = _TOBMaxLimitPlaceHolder[0][0];
                        if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                        {
                            // OPC-UA code to set the value at the node id = ID
                            (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                            outputs[0].nameSpace = id.NameSpace;
                            outputs[0].id = id.ID;
                            outputs[0].value = TOB;
                            outputs[0].sourceTimestamp = DateTime.UtcNow;
                            bool ok = _DWISClient.UpdateAnyVariables(outputs);
                            if (ok)
                            {
                                _logger?.LogInformation("TOB: " + (TOB ).ToString("F1"));
                            }
                        }
                    }
                    if (_DWISClient != null && _DPMaxLimitPlaceHolder != null && _DPMaxLimitPlaceHolder.Count > 0 && _DPMaxLimitPlaceHolder[0].Count > 0)
                    {
                        NodeIdentifier id = _DPMaxLimitPlaceHolder[0][0];
                        if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                        {
                            // OPC-UA code to set the value at the node id = ID
                            (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                            outputs[0].nameSpace = id.NameSpace;
                            outputs[0].id = id.ID;
                            outputs[0].value = DP;
                            outputs[0].sourceTimestamp = DateTime.UtcNow;
                            bool ok = _DWISClient.UpdateAnyVariables(outputs);
                            if (ok)
                            {
                                _logger?.LogInformation("dP: " + (DP/1e5).ToString("F1"));
                            }
                        }
                    }

                }
            }
        }
    }
}
