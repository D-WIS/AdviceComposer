using DWIS.API.DTO;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.Client.ReferenceImplementation;
using DWIS.RigOS.Capabilities.Controller.Model;
using DWIS.RigOS.Common.Model;
using Newtonsoft.Json;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using System.Reflection;
using DWIS.Scheduler.Model;
using DWIS.Vocabulary.Schemas;
using DWIS.MicroState.Model;
using System.Diagnostics.Eventing.Reader;

namespace DWIS.AdviceComposer.SchedulerROPContext.Test
{
    public class Worker : BackgroundService
    {
        private static JsonSerializerSettings _JSonSerializationSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
            Formatting = Formatting.Indented
        };
        private object lock_ = new object();

        private ILogger<DWISClientOPCF>? _loggerDWISClient;
        private ILogger<Worker>? _logger;
        private IOPCUADWISClient? _DWISClient = null;
        private TimeSpan _loopSpan;

        private Configuration Configuration { get; set; } = new Configuration();

        private QueryResult? _contextPlaceHolder = null;

        private Dictionary<string, (string sparql, string key)> registeredQueriesSparqls_ = new Dictionary<string, (string sparql, string key)>();

        private List<AcquiredSignals> placeHolders_ = new List<AcquiredSignals>();

        private static string _ADCSStandardInterfaceSubscriptionName = "ADCSStandardInterfaceSubscription";
        private Dictionary<string, Entry> RegisteredQueries { get; set; } = new Dictionary<string, Entry>();
        private Guid MicroStateID { get; set; } = Guid.Empty;

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

        private bool RegisterToBlackboard(SemanticInfo? semanticInfo, IOPCUADWISClient? DWISClient, ref QueryResult? placeHolder)
        {
            bool ok = false;
            if (DWISClient != null && semanticInfo != null && semanticInfo.Manifest != null && !string.IsNullOrEmpty(semanticInfo.SparQLQuery) && semanticInfo.SparQLVariables != null && semanticInfo.SparQLVariables.Count > 0)
            {
                ManifestFile manifestFile = semanticInfo.Manifest;
                string sparQLQuery = semanticInfo.SparQLQuery;
                List<string>? variables = semanticInfo.SparQLVariables.ToList<string>();
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
                Assembly? assembly = Assembly.GetAssembly(typeof(ADCSStandardInterfaceHelper));
                var queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, typeof(ADCSStandardInterfaceHelper).FullName);
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
                if (_DWISClient != null && _DWISClient.Connected)
                {
                    MicroStateID = Guid.NewGuid();
                    RegisterQuery(new MicroStates(), RegisteredQueries, MicroStateID);
                }
                await Loop(cancellationToken);
            }
        }
        private void RegisterQuery(MicroStates? microStates, Dictionary<string, Entry> dict, Guid key)
        {
            if (_DWISClient != null && _DWISClient.Connected && microStates != null)
            {
                Type type = microStates.GetType();
                Assembly assembly = type.Assembly;
                Dictionary<string, QuerySpecification>? queries = GeneratorSparQLManifestFile.GetSparQLQueries(assembly, type.FullName);
                if (queries != null &&
                    queries.Count > 0 &&
                    queries.First().Value != null &&
                    !string.IsNullOrEmpty(queries.First().Value.SparQL) &&
                    queries.First().Value.Variables != null &&
                    queries.First().Value.Variables!.Count > 0)
                {
                    string sparql = queries.First().Value.SparQL!;
                    var result = _DWISClient.RegisterQuery(sparql, RegisterQueryCallbackSingleVariable);

                    if (!string.IsNullOrEmpty(result.jsonQueryDiff))
                    {
                        var queryDiff = QueryResultsDiff.FromJsonString(result.jsonQueryDiff);
                        if (queryDiff != null && !string.IsNullOrEmpty(queryDiff.QueryID) && RegisteredQueries != null)
                        {
                            lock (lock_)
                            {
                                if (!RegisteredQueries.ContainsKey(queryDiff.QueryID))
                                {
                                    Entry entry = new Entry() { sparql = sparql, Key = key };
                                    RegisteredQueries.Add(queryDiff.QueryID, entry);
                                }
                            }
                            if (queryDiff.Added != null && queryDiff.Added.Any())
                            {
                                ManageQueryDiff(queryDiff);
                            }
                        }
                    }
                }
            }
        }
        private void RegisterQueryCallbackSingleVariable(QueryResultsDiff queryDiff)
        {
            _logger?.LogInformation("Callback for register query");
            if (_DWISClient != null && _DWISClient.Connected && queryDiff != null)
            {
                ManageQueryDiff(queryDiff);
            }
        }
        private void ManageQueryDiff(QueryResultsDiff queryDiff)
        {
            lock (lock_)
            {
                if (RegisteredQueries.ContainsKey(queryDiff.QueryID))
                {
                    var entry = RegisteredQueries[queryDiff.QueryID];
                    if (entry.Results == null)
                    {
                        entry.Results = new List<QueryResultRow>();
                    }
                    if (queryDiff.Removed != null)
                    {
                        foreach (QueryResultRow row in queryDiff.Removed)
                        {
                            entry.Results.Remove(row);
                        }
                    }
                    if (queryDiff.Added != null)
                    {
                        foreach (QueryResultRow row in queryDiff.Added)
                        {
                            entry.Results.Add(row);
                        }
                    }
                    // this code supposes that the first variable of the sparql query is an OPC-UA live variable
                    List<NodeIdentifier> nodes = new List<NodeIdentifier>();
                    foreach (var row in entry.Results)
                    {
                        if (row != null && row.Items != null && row.Items.Count > 0)
                        {
                            NodeIdentifier node = row.Items[0]; // this is where it is supposed that the first variable of the query is an OPC-UA live variable
                            if (node != null)
                            {
                                if (!nodes.Exists(n => n.ID == node.ID && n.NameSpace == node.NameSpace))
                                {
                                    nodes.Add(node);
                                }
                            }
                        }
                    }
                    if (_DWISClient != null)
                    {
                        foreach (NodeIdentifier node in nodes)
                        {
                            if (!entry.LiveValues.Values.Any(v => v.ns == node.NameSpace && v.id == node.ID))
                            {
                                Guid guid = Guid.NewGuid();
                                LiveValue liveValue = new(node.NameSpace, node.ID, null);
                                entry.LiveValues.Add(guid, liveValue);
                                _DWISClient.Subscribe(entry, CallbackOPCUA, new (string, string, object)[] { new(liveValue.ns, liveValue.id, guid) });
                            }
                        }
                    }
                }
            }
        }

        private void CallbackOPCUA(object subscriptionData, UADataChange[] changes)
        {
            if (subscriptionData != null && subscriptionData is Entry entry && entry.LiveValues != null && changes != null && changes.Length > 0)
            {
                UADataChange dataChange = changes[0];
                if (dataChange != null && entry.LiveValues.Count > 0)
                {
                    LiveValue? lv = entry.LiveValues.First().Value;
                    if (lv != null)
                    {
                        lv.val = dataChange.Value;
                    }
                }
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
            QueryResult? enablePlaceHolder = null;
            PeriodicTimer timer = new PeriodicTimer(_loopSpan);
            DateTime start = DateTime.UtcNow;
            bool? previousInsideHardFormation = null;
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (placeHolders_ != null && autodriller == null)
                {
                    var existing = placeHolders_.FirstOrDefault(ph => ph.Any() && ph.First().Key == _ADCSStandardInterfaceSubscriptionName);
                    if (existing != null && existing.TryGetValue(_ADCSStandardInterfaceSubscriptionName, out List<AcquiredSignal>? signals))
                    {
                        if (signals != null && signals.Count > 0)
                        {
                            foreach (var signal in signals)
                            {
                                string? js = signal.GetValue<string>();
                                if (!string.IsNullOrEmpty(js))
                                {
                                    try
                                    {
                                        var settings = new JsonSerializerSettings
                                        {
                                            TypeNameHandling = TypeNameHandling.Objects,
                                            Formatting = Formatting.Indented
                                        };
                                        ActivableFunction? activableFunction = Newtonsoft.Json.JsonConvert.DeserializeObject<ActivableFunction>(js, settings);
                                        if (activableFunction != null && activableFunction is ADCSStandardAutoDriller autoDriller)
                                        {
                                            // read any information that may be useful for the advisor about the ADCS actual capabilities
                                            autodriller = autoDriller;
                                            if (autodriller.EnableFunction != null)
                                            {
                                                RegisterToBlackboard(autodriller.EnableFunction, _DWISClient, ref enablePlaceHolder);
                                            }
                                            break;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        _logger?.LogError(e.ToString());
                                    }
                                }
                            }
                        }
                    }
                }
                TimeSpan elapsed = start - DateTime.UtcNow;
                if (_DWISClient != null && autodriller != null && _contextPlaceHolder == null)
                {
                    if (autodriller.ContextSemanticInfo != null && !string.IsNullOrEmpty(autodriller.ContextSemanticInfo.SparQLQuery) && autodriller.ContextSemanticInfo.SparQLVariables != null && autodriller.ContextSemanticInfo.Manifest != null)
                    {
                        RegisterToBlackboard(autodriller.ContextSemanticInfo, _DWISClient, ref _contextPlaceHolder);
                    }
                }
                string json = string.Empty;
                lock (lock_)
                {
                    if (RegisteredQueries != null && MicroStateID != Guid.Empty)
                    {
                        Entry? entry = FindEntry(MicroStateID);
                        if (entry != null)
                        {
                            if (entry.LiveValues != null && entry.LiveValues.Count > 0)
                            {
                                LiveValue? lv = entry.LiveValues.First().Value;
                                if (lv != null)
                                {
                                    if (lv.val != null && lv.val is string sval)
                                    {
                                        json = sval;
                                    }
                                }
                            }
                        }
                    }
                }
                MicroStates? microStates = null;
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        microStates = JsonConvert.DeserializeObject<MicroStates>(json, _JSonSerializationSettings);
                    }
                    catch (Exception e)
                    {

                    }
                }
                bool insideHardFormation = false;
                if (microStates != null)
                {
                    byte changeFormation = microStates.Value.GetValue(MicroStateIndex.FormationChange);
                    byte hardFormation = microStates.Value.GetValue(MicroStateIndex.InsideHardStringer);
                    insideHardFormation = hardFormation == 2;
                }
                if (_DWISClient != null && _contextPlaceHolder != null && _contextPlaceHolder.Count > 0 && _contextPlaceHolder[0] != null && _contextPlaceHolder[0].Count > 0)
                {
                    if (insideHardFormation != previousInsideHardFormation)
                    {
                        DWISContext context = new DWISContext();
                        if (context.CapabilityPreferences == null)
                        {
                            context.CapabilityPreferences = new List<Nouns.Enum>();
                        }
                        context.CapabilityPreferences.Clear();
                        if (insideHardFormation)
                        {
                            context.CapabilityPreferences.Add(Nouns.Enum.RigActionPlanFeature);
                            context.CapabilityPreferences.Add(Nouns.Enum.DrillStemVibrationFeature);
                        }
                        else
                        {
                            context.CapabilityPreferences.Add(Nouns.Enum.RigActionPlanFeature);
                            context.CapabilityPreferences.Add(Nouns.Enum.CuttingsTransportFeature);
                        }
                        var settings = new JsonSerializerSettings
                        {
                            TypeNameHandling = TypeNameHandling.Objects,
                            Formatting = Formatting.Indented
                        };
                        json = Newtonsoft.Json.JsonConvert.SerializeObject(context, settings);
                        if (!string.IsNullOrEmpty(json))
                        {
                            NodeIdentifier id = _contextPlaceHolder[0][0];
                            if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                            {
                                // OPC-UA code to set the value at the node id = ID
                                (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                                outputs[0].nameSpace = id.NameSpace;
                                outputs[0].id = id.ID;
                                outputs[0].value = json;
                                outputs[0].sourceTimestamp = DateTime.UtcNow;
                                bool ok = _DWISClient.UpdateAnyVariables(outputs);
                                if (ok)
                                {
                                    string description = string.Empty;
                                    foreach (var choice in context.CapabilityPreferences)
                                    {
                                        description += choice.ToString() + ", ";
                                    }
                                    _logger?.LogInformation("context changed to: " + description);
                                }
                            }
                        }
                        previousInsideHardFormation = insideHardFormation;
                    }
                }
            }
        }
        private Entry? FindEntry(Guid id)
        {
            Entry? entry = null;
            if (RegisteredQueries != null)
            {
                foreach (var kvp in RegisteredQueries)
                {
                    if (kvp.Value.Key == id)
                    {
                        entry = kvp.Value;
                        break;
                    }
                }
            }
            return entry;
        }
    }
}
