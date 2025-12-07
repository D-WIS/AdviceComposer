using DWIS.Client.ReferenceImplementation;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using DWIS.API.DTO;
using DWIS.AdviceComposer.Model;
using DWIS.RigOS.Common.Model;
using DWIS.RigOS.Capabilities.Controller.Model;
using Newtonsoft.Json;
using DWIS.Scheduler.Model;
using System.Diagnostics;
using Org.BouncyCastle.Bcpg.Sig;
using System.Runtime.Intrinsics.Arm;
using OSDC.DotnetLibraries.General.Common;
using System;
using System.Reflection;
using OSDC.DotnetLibraries.Drilling.DrillingProperties;
using Opc.Ua;
using DWIS.RigOS.Capabilities.Procedure.Model;
using DWIS.RigOS.Capabilities.FDIR.Model;
using DWIS.RigOS.Capabilities.SOE.Model;
using DWIS.RigOS.Capabilities.SOE.Model;
using DWIS.RigOS.Capabilities.FDIR.Model;

namespace DWIS.AdviceComposer.Service
{
    public class Worker : BackgroundService
    {
        private ILogger<DWISClientOPCF>? _loggerDWISClient;
        private ILogger<Worker>? _logger;
        private IOPCUADWISClient? _DWISClient = null;
        private TimeSpan _loopSpan;
        private Configuration Configuration { get; set; } = new Configuration();

        private Dictionary<Guid, QueryResult> PlaceHolders { get; set; } = new Dictionary<Guid, QueryResult>();

        private Dictionary<Guid, (ControlFunctionData src, ControllerFunctionData? last, DateTime lastTimeStamp)> ControlFunctionDictionary { get; set; } = new Dictionary<Guid, (ControlFunctionData src, ControllerFunctionData? last, DateTime lastTimeStamp)>();
        private Dictionary<Guid, (ProcedureFunctionData src, ProcedureFunctionData? last, DateTime lastTimeStamp)> ProcedureFunctionDictionary { get; set; } = new Dictionary<Guid, (ProcedureFunctionData src, ProcedureFunctionData? last, DateTime lastTimeStamp)>();
        private Dictionary<Guid, (FaultDetectionIsolationAndRecoveryFunctionData src, FaultDetectionIsolationAndRecoveryFunctionData? last, DateTime lastTimeStamp)> FaultDetectionIsolationAndRecoveryFunctionDictionary { get; set; } = new Dictionary<Guid, (FaultDetectionIsolationAndRecoveryFunctionData src, FaultDetectionIsolationAndRecoveryFunctionData? last, DateTime lastTimeStamp)>();
        private Dictionary<Guid, (SafeOperatingEnvelopeFunctionData src, SafeOperatingEnvelopeFunctionData? last, DateTime lastTimeStamp)> SafeOperatingEnvelopeFunctionDictionary { get; set; } = new Dictionary<Guid, (SafeOperatingEnvelopeFunctionData src, SafeOperatingEnvelopeFunctionData? last, DateTime lastTimeStamp)>();

        private Dictionary<string, Entry> RegisteredQueries { get; set; } = new Dictionary<string, Entry>();

        private object lock_ = new object();
        private static readonly JsonSerializerSettings _contextSerializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Objects,
            Formatting = Formatting.Indented
        };

        private Guid _ADCSStandardInterfaceSubscription = Guid.NewGuid();
        private static string _prefix = "DWIS:AdviceComposer:";
        private static string _companyName = "DWIS";

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
                                Entry userDataEntry = new Entry();
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
                    if (dataChange.UserData != null && dataChange.UserData is Guid guid)
                    {
                        if (entry.LiveValues.ContainsKey(guid))
                        {
                            entry.LiveValues[guid].val = dataChange.Value;
                        }
                    }
                    else 
                    {
                    }
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ConnectToBlackboard();

            if (_DWISClient != null && _DWISClient.Connected)
            {
                AdviceComposer.Model.ADCSStandardInterfaceHelper standardInterfaceHelper = new AdviceComposer.Model.ADCSStandardInterfaceHelper();
                if (_DWISClient != null && _DWISClient.Connected && standardInterfaceHelper != null && !string.IsNullOrEmpty(standardInterfaceHelper.SparQLQuery))
                {
                    string sparql = standardInterfaceHelper.SparQLQuery;
                    var r = _DWISClient.GetQueryResult(sparql);
                    var result = _DWISClient.RegisterQuery(sparql, RegisterQueryCallbackSingleVariable);
                    if (!string.IsNullOrEmpty(result.jsonQueryDiff))
                    {
                        var queryDiff = QueryResultsDiff.FromJsonString(result.jsonQueryDiff);
                        if (queryDiff != null)
                        {
                            Entry entry = new Entry() { sparql = sparql, Key = _ADCSStandardInterfaceSubscription };
                            lock (lock_)
                            {
                                RegisteredQueries.Add(queryDiff.QueryID, entry);
                            }
                            ManageQueryDiff(queryDiff);
                        }
                    }
                }
                await Loop(cancellationToken);
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

        private void RegisterQueryCallbackMultipleVariables(QueryResultsDiff queryDiff)
        {
            _logger?.LogInformation("Callback for register query");
            if (_DWISClient != null && _DWISClient.Connected && queryDiff != null)
            {
                ManageQueryDiff(queryDiff);
            }
        }

        private void RegisterQuery(SemanticInfo? semanticInfo, Dictionary<string, Entry> dict, Guid key)
        {
            if (_DWISClient != null && _DWISClient.Connected && semanticInfo != null && !string.IsNullOrEmpty(semanticInfo.SparQLQuery) && semanticInfo.SparQLVariables != null && semanticInfo.SparQLVariables.Count > 0)
            {
                string sparql = semanticInfo.SparQLQuery;
                var result = _DWISClient.RegisterQuery(sparql, RegisterQueryCallbackSingleVariable);
                if (!string.IsNullOrEmpty(result.jsonQueryDiff))
                {
                    var queryDiff = QueryResultsDiff.FromJsonString(result.jsonQueryDiff);
                    if (queryDiff != null && !string.IsNullOrEmpty(queryDiff.QueryID))
                    {
                        Entry entry = new Entry() { sparql = sparql, Key = key };
                        lock (lock_)
                        {
                            dict.Add(queryDiff.QueryID, entry);
                        }
                        if (queryDiff.Added != null && queryDiff.Added.Any())
                        {
                            ManageQueryDiff(queryDiff);
                        }
                    }
                }
            }
        }

        private void RegisterQueryAlternate(SemanticInfo? semanticInfo, Dictionary<string, Entry> dict, Guid key)
        {
            if (_DWISClient != null &&
                _DWISClient.Connected &&
                semanticInfo != null &&
                !string.IsNullOrEmpty(semanticInfo.SparQLAlternateQuery) &&
                semanticInfo.SparQLAlternateVariables != null &&
                semanticInfo.SparQLAlternateVariables.Count > 0)
            {
                string sparql = semanticInfo.SparQLAlternateQuery;
                var result = _DWISClient.RegisterQuery(sparql, RegisterQueryCallbackMultipleVariables);
                if (!string.IsNullOrEmpty(result.jsonQueryDiff))
                {
                    var queryDiff = QueryResultsDiff.FromJsonString(result.jsonQueryDiff);
                    if (queryDiff != null && !string.IsNullOrEmpty(queryDiff.QueryID))
                    {
                        Entry entry = new Entry() { sparql = sparql, Key = key };
                        lock (lock_)
                        {
                            dict.Add(queryDiff.QueryID, entry);
                        }
                        if (queryDiff.Added != null && queryDiff.Added.Any())
                        {
                            ManageQueryDiff(queryDiff);
                        }
                    }
                }
            }
        }

        private bool RegisterToBlackboard(TokenizedAssignableReference<ScalarValue>? assignable, IOPCUADWISClient? DWISClient, ref QueryResult? placeHolder)
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


        private async Task Loop(CancellationToken cancellationToken)
        {
            PeriodicTimer timer = new PeriodicTimer(_loopSpan);

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                ManageActivableFunctionList();
                ManageControllerFunctionsSetPointsLimitsAndParameters();
                ManageProcedureParameters();
                ManageFaultDetectionIsolationAndRecoveryParameters();
                ManageSafeOperatingEnvelopeParameters();
            }
        }

        private void ManageActivableFunctionList()
        {
            Dictionary<string, Entry>? queries = null;
            lock (lock_)
            {
                queries = new Dictionary<string, Entry>(RegisteredQueries);
            }
            if (queries != null)
            {
                foreach (var kvp in queries)
                {
                    if (kvp.Value != null && kvp.Value.Key == _ADCSStandardInterfaceSubscription && kvp.Value.Results != null && kvp.Value.LiveValues != null && kvp.Value.LiveValues.Count > 0)
                    {
                        foreach (var kvp2 in kvp.Value.LiveValues)
                        {
                            if (kvp2.Value != null && kvp2.Value.val != null && kvp2.Value.val is string json)
                            {
                                if (json != null)
                                {
                                    try
                                    {
                                        var settings = new JsonSerializerSettings
                                        {
                                            TypeNameHandling = TypeNameHandling.Objects,
                                            Formatting = Formatting.Indented
                                        };
                                        ActivableFunction? activableFunction = JsonConvert.DeserializeObject<ActivableFunction>(json, settings);
                                        if (activableFunction != null && !string.IsNullOrEmpty(activableFunction.Name))
                                        {
                                            if (activableFunction is ControllerFunction controllerFunction)
                                            {
                                                ManageControllerFunction(controllerFunction, activableFunction);
                                            }
                                            else if (activableFunction is ProcedureFunction procedureFunction)
                                            {
                                                ManageProcedureFunction(procedureFunction, activableFunction);
                                            }
                                            else if (activableFunction is FaultDetectionIsolationAndRecoveryFunction fdirFunction)
                                            {
                                                ManageFaultDetectionIsolationAndRecoveryFunction(fdirFunction, activableFunction);
                                            }
                                            else if (activableFunction is SafeOperatingEnvelopeFunction soeFunction)
                                            {
                                                ManageSafeOperatingEnvelopeFunction(soeFunction, activableFunction);
                                            }
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
            }
        }
        private void ManageProcedureFunction(ProcedureFunction procedureFunction, ActivableFunction activableFunction)
        {
            bool found = false;
            foreach (var kvp1 in ProcedureFunctionDictionary)
            {
                if (kvp1.Value.src != null && kvp1.Value.src.ProcedureFunction != null && !string.IsNullOrEmpty(kvp1.Value.src.ProcedureFunction.Name) && kvp1.Value.src.ProcedureFunction.Name.Equals(activableFunction.Name))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Guid procedureFunctionID = Guid.NewGuid();
                ProcedureFunctionData procedureFunctionData = new ProcedureFunctionData();
                procedureFunctionData.ProcedureFunction = procedureFunction;
                ProcedureFunctionDictionary.Add(procedureFunctionID, new(procedureFunctionData, null, DateTime.MinValue));

                if (procedureFunction.ContextSemanticInfo != null)
                {
                    if (string.IsNullOrEmpty(procedureFunction.ContextSemanticInfo.SparQLQuery) || procedureFunction.ContextSemanticInfo.SparQLVariables == null)
                    {
                        procedureFunction.FillInSparqlQueriesAndManifests();
                    }
                    procedureFunctionData.ContextID = Guid.NewGuid();
                    RegisterQuery(procedureFunction.ContextSemanticInfo, RegisteredQueries, procedureFunctionData.ContextID);
                }
                if (procedureFunction.Parameters != null)
                {
                    if (string.IsNullOrEmpty(procedureFunction.Parameters.SparQLQuery) || procedureFunction.Parameters.SparQLVariables == null)
                    {
                        procedureFunction.FillInSparqlQueriesAndManifests();
                    }
                    procedureFunctionData.ParametersID = Guid.NewGuid();
                    RegisterQueryAlternate(procedureFunction.Parameters, RegisteredQueries, procedureFunctionData.ParametersID);
                }
                if (procedureFunction.Parameters != null &&
                    !string.IsNullOrEmpty(procedureFunction.Parameters.SparQLQuery))
                {
                    if (string.IsNullOrEmpty(procedureFunction.Parameters.SparQLQuery) || procedureFunction.Parameters.SparQLVariables == null)
                    {
                        procedureFunction.FillInSparqlQueriesAndManifests();
                    }
                    QueryResult? result = null;
                    RegisterToBlackboard(procedureFunction.Parameters, _DWISClient, ref result);
                    if (result != null)
                    {
                        procedureFunctionData.ParametersDestinationID = Guid.NewGuid();
                        PlaceHolders.Add(procedureFunctionData.ParametersDestinationID, result);
                    }
                }                
            }
        }

        private void ManageFaultDetectionIsolationAndRecoveryFunction(FaultDetectionIsolationAndRecoveryFunction fdirFunction, ActivableFunction activableFunction)
        {
            bool found = false;
            foreach (var kvp1 in FaultDetectionIsolationAndRecoveryFunctionDictionary)
            {
                if (kvp1.Value.src != null && kvp1.Value.src.FaultDetectionIsolationAndRecoveryFunction != null && !string.IsNullOrEmpty(kvp1.Value.src.FaultDetectionIsolationAndRecoveryFunction.Name) && kvp1.Value.src.FaultDetectionIsolationAndRecoveryFunction.Name.Equals(activableFunction.Name))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Guid fdirFunctionID = Guid.NewGuid();
                FaultDetectionIsolationAndRecoveryFunctionData fdirFunctionData = new FaultDetectionIsolationAndRecoveryFunctionData();
                fdirFunctionData.FaultDetectionIsolationAndRecoveryFunction = fdirFunction;
                FaultDetectionIsolationAndRecoveryFunctionDictionary.Add(fdirFunctionID, new(fdirFunctionData, null, DateTime.MinValue));

                if (fdirFunction.ContextSemanticInfo != null)
                {
                    if (string.IsNullOrEmpty(fdirFunction.ContextSemanticInfo.SparQLQuery) || fdirFunction.ContextSemanticInfo.SparQLVariables == null)
                    {
                        fdirFunction.FillInSparqlQueriesAndManifests();
                    }
                    fdirFunctionData.ContextID = Guid.NewGuid();
                    RegisterQuery(fdirFunction.ContextSemanticInfo, RegisteredQueries, fdirFunctionData.ContextID);
                }
                if (fdirFunction.Parameters != null)
                {
                    if (string.IsNullOrEmpty(fdirFunction.Parameters.SparQLQuery) || fdirFunction.Parameters.SparQLVariables == null)
                    {
                        fdirFunction.FillInSparqlQueriesAndManifests();
                    }
                    fdirFunctionData.ParametersID = Guid.NewGuid();
                    RegisterQueryAlternate(fdirFunction.Parameters, RegisteredQueries, fdirFunctionData.ParametersID);
                }
                if (fdirFunction.Parameters != null &&
                    !string.IsNullOrEmpty(fdirFunction.Parameters.SparQLQuery))
                {
                    if (string.IsNullOrEmpty(fdirFunction.Parameters.SparQLQuery) || fdirFunction.Parameters.SparQLVariables == null)
                    {
                        fdirFunction.FillInSparqlQueriesAndManifests();
                    }
                    QueryResult? result = null;
                    RegisterToBlackboard(fdirFunction.Parameters, _DWISClient, ref result);
                    if (result != null)
                    {
                        fdirFunctionData.ParametersDestinationID = Guid.NewGuid();
                        PlaceHolders.Add(fdirFunctionData.ParametersDestinationID, result);
                    }
                }
            }
        }

        private void ManageSafeOperatingEnvelopeFunction(SafeOperatingEnvelopeFunction soeFunction, ActivableFunction activableFunction)
        {
            bool found = false;
            foreach (var kvp1 in SafeOperatingEnvelopeFunctionDictionary)
            {
                if (kvp1.Value.src != null && kvp1.Value.src.SafeOperatingEnvelopeFunction != null && !string.IsNullOrEmpty(kvp1.Value.src.SafeOperatingEnvelopeFunction.Name) && kvp1.Value.src.SafeOperatingEnvelopeFunction.Name.Equals(activableFunction.Name))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Guid soeFunctionID = Guid.NewGuid();
                SafeOperatingEnvelopeFunctionData soeFunctionData = new SafeOperatingEnvelopeFunctionData();
                soeFunctionData.SafeOperatingEnvelopeFunction = soeFunction;
                SafeOperatingEnvelopeFunctionDictionary.Add(soeFunctionID, new(soeFunctionData, null, DateTime.MinValue));

                if (soeFunction.ContextSemanticInfo != null)
                {
                    if (string.IsNullOrEmpty(soeFunction.ContextSemanticInfo.SparQLQuery) || soeFunction.ContextSemanticInfo.SparQLVariables == null)
                    {
                        soeFunction.FillInSparqlQueriesAndManifests();
                    }
                    soeFunctionData.ContextID = Guid.NewGuid();
                    RegisterQuery(soeFunction.ContextSemanticInfo, RegisteredQueries, soeFunctionData.ContextID);
                }
                if (soeFunction.Parameters != null)
                {
                    if (string.IsNullOrEmpty(soeFunction.Parameters.SparQLQuery) || soeFunction.Parameters.SparQLVariables == null)
                    {
                        soeFunction.FillInSparqlQueriesAndManifests();
                    }
                    soeFunctionData.ParametersID = Guid.NewGuid();
                    RegisterQueryAlternate(soeFunction.Parameters, RegisteredQueries, soeFunctionData.ParametersID);
                }
                if (soeFunction.Parameters != null &&
                    !string.IsNullOrEmpty(soeFunction.Parameters.SparQLQuery))
                {
                    if (string.IsNullOrEmpty(soeFunction.Parameters.SparQLQuery) || soeFunction.Parameters.SparQLVariables == null)
                    {
                        soeFunction.FillInSparqlQueriesAndManifests();
                    }
                    QueryResult? result = null;
                    RegisterToBlackboard(soeFunction.Parameters, _DWISClient, ref result);
                    if (result != null)
                    {
                        soeFunctionData.ParametersDestinationID = Guid.NewGuid();
                        PlaceHolders.Add(soeFunctionData.ParametersDestinationID, result);
                    }
                }
            }
        }

        private void ManageControllerFunction(ControllerFunction controllerFunction, ActivableFunction activableFunction)
        {
            if (ControlFunctionAlreadyRegistered(activableFunction.Name))
            {
                return;
            }

            Guid controlFunctionID = Guid.NewGuid();
            ControlFunctionData controlFunctionData = new ControlFunctionData();
            controlFunctionData.ControllerFunction = controllerFunction;
            ControlFunctionDictionary.Add(controlFunctionID, new(controlFunctionData, null, DateTime.MinValue));

            RegisterControllerContext(controllerFunction, controlFunctionData);
            RegisterControllerParameters(controllerFunction, controlFunctionData);
            RegisterControllers(controllerFunction, controlFunctionData);
        }
        private void ManageLimits(Dictionary<string, ControllerLimit>? controllerLimits, ControlData controllerData)
        {
            if (controllerLimits != null && controllerData != null)
            {
                foreach (var kpv in controllerLimits)
                {
                    LimitData limitData = new LimitData();
                    controllerData.LimitIDs.Add(limitData);
                    if (kpv.Value != null)
                    {
                        ManageLimit(kpv.Value, limitData);
                    }
                }
            }
        }

        private void ManageLimits(Dictionary<string, ControllerLimitWithControl>? controllerLimits, ControlData controllerData)
        {
            if (controllerLimits != null && controllerData != null)
            {
                foreach (var kpv in controllerLimits)
                {
                    LimitData limitData = new LimitData();
                    controllerData.LimitIDs.Add(limitData);
                    if (kpv.Value != null)
                    {
                        ManageLimit(kpv.Value, limitData);
                    }
                }
            }
        }

        private void ManageLimit(ControllerLimit? limit, LimitData limitData)
        {
            if (limit != null)
            {
                limitData.IsMin = limit.IsMinReference;
                if (limit.LimitValueReference != null &&
                    !string.IsNullOrEmpty(limit.LimitValueReference.SparQLAlternateQuery) &&
                    limit.LimitValueReference.SparQLAlternateVariables != null &&
                    limit.LimitValueReference.SparQLAlternateVariables.Count > 0)
                {
                    limitData.MaxLimitSourceID = Guid.NewGuid();
                    RegisterQueryAlternate(limit.LimitValueReference, RegisteredQueries, limitData.MaxLimitSourceID);
                }
                if (limit.LimitValueReference != null &&
                    !string.IsNullOrEmpty(limit.LimitValueReference.SparQLQuery) &&
                    limit.LimitValueReference.SparQLVariables != null &&
                    limit.LimitValueReference.SparQLVariables.Count > 0)
                {
                    QueryResult? result = null;
                    RegisterToBlackboard(limit.LimitValueReference, _DWISClient, ref result);
                    if (result != null)
                    {
                        limitData.MaxLimitDestinationID = Guid.NewGuid();
                        PlaceHolders.Add(limitData.MaxLimitDestinationID, result);
                    }
                }
                if (limit.LimitControlledVariableReference != null &&
                    !string.IsNullOrEmpty(limit.LimitControlledVariableReference.SparQLAlternateQuery) &&
                    limit.LimitControlledVariableReference.SparQLAlternateVariables != null &&
                    limit.LimitControlledVariableReference.SparQLAlternateVariables.Count > 0)
                {
                    limitData.MeasuredValueID = Guid.NewGuid();
                    RegisterQueryAlternate(limit.LimitControlledVariableReference, RegisteredQueries, limitData.MeasuredValueID);
                }
                if (limit.LimitValueMaxRateOfChange != null &&
                    !string.IsNullOrEmpty(limit.LimitValueMaxRateOfChange.SparQLQuery) &&
                    limit.LimitValueMaxRateOfChange.SparQLVariables != null &&
                    limit.LimitValueMaxRateOfChange.SparQLVariables.Count > 0)
                {
                    limitData.MaxRateOfChangeID = Guid.NewGuid();
                    RegisterQuery(limit.LimitValueMaxRateOfChange, RegisteredQueries, limitData.MaxRateOfChangeID);
                }
            }
        }
        private void ManageProcedureParameters()
        {
            if (ProcedureFunctionDictionary != null && PlaceHolders != null && RegisteredQueries != null)
            {
                foreach (var kvp in ProcedureFunctionDictionary)
                {
                    if (kvp.Value.src != null)
                    {
                        Entry? entryContext = null;
                        if (kvp.Value.src.ContextID != Guid.Empty)
                        {
                            entryContext = GetEntry(kvp.Value.src.ContextID);
                        }
                        if (entryContext != null &&
                            entryContext.LiveValues != null &&
                            entryContext.LiveValues.Count > 0 &&
                            TryGetContextFeatures(entryContext, out List<Vocabulary.Schemas.Nouns.Enum> features))
                        {
                            Dictionary<string, ProcedureData> availableDatas = new Dictionary<string, ProcedureData>();
                            ManageParameters(kvp.Value.src, availableDatas);
                            ProcedureData? chosenProcedureFunction = ChooseProcedureFunction(availableDatas, features);
                            if (chosenProcedureFunction != null && chosenProcedureFunction.Parameters != null && chosenProcedureFunction.ParametersDestinationQueryResult != null)
                            {
                                SendValue(chosenProcedureFunction.ParametersDestinationQueryResult, chosenProcedureFunction.Parameters);
                            }
                            break;
                        }
                    }
                }
            }
        }
        private void ManageFaultDetectionIsolationAndRecoveryParameters()
        {
            if (FaultDetectionIsolationAndRecoveryFunctionDictionary != null && PlaceHolders != null && RegisteredQueries != null)
            {
                foreach (var kvp in FaultDetectionIsolationAndRecoveryFunctionDictionary)
                {
                    if (kvp.Value.src != null)
                    {
                        Entry? entryContext = null;
                        if (kvp.Value.src.ContextID != Guid.Empty)
                        {
                            entryContext = GetEntry(kvp.Value.src.ContextID);
                        }
                        if (entryContext != null &&
                            entryContext.LiveValues != null &&
                            entryContext.LiveValues.Count > 0 &&
                            TryGetContextFeatures(entryContext, out List<Vocabulary.Schemas.Nouns.Enum> features))
                        {
                            Dictionary<string, FaultDetectionIsolationAndRecoveryData> availableDatas = new Dictionary<string, FaultDetectionIsolationAndRecoveryData>();
                            ManageParameters(kvp.Value.src, availableDatas);
                            FaultDetectionIsolationAndRecoveryData? chosen = ChooseFaultDetectionIsolationAndRecoveryFunction(availableDatas, features);
                            if (chosen != null && chosen.Parameters != null && chosen.ParametersDestinationQueryResult != null)
                            {
                                SendValue(chosen.ParametersDestinationQueryResult, chosen.Parameters);
                            }
                            break;
                        }
                    }
                }
            }
        }
        private void ManageSafeOperatingEnvelopeParameters()
        {
            if (SafeOperatingEnvelopeFunctionDictionary != null && PlaceHolders != null && RegisteredQueries != null)
            {
                foreach (var kvp in SafeOperatingEnvelopeFunctionDictionary)
                {
                    if (kvp.Value.src != null)
                    {
                        Entry? entryContext = null;
                        if (kvp.Value.src.ContextID != Guid.Empty)
                        {
                            entryContext = GetEntry(kvp.Value.src.ContextID);
                        }
                        if (entryContext != null &&
                            entryContext.LiveValues != null &&
                            entryContext.LiveValues.Count > 0 &&
                            TryGetContextFeatures(entryContext, out List<Vocabulary.Schemas.Nouns.Enum> features))
                        {
                            Dictionary<string, SafeOperatingEnvelopeData> availableDatas = new Dictionary<string, SafeOperatingEnvelopeData>();
                            ManageParameters(kvp.Value.src, availableDatas);
                            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, SafeOperatingEnvelopeData data)> candidates = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, SafeOperatingEnvelopeData data)>();
                            SelectSafeOperatingEnvelopeFunctionData(availableDatas, features, candidates);
                            SafeOperatingEnvelopeData? combined = CombineSafeOperatingEnvelopes(candidates);
                            if (combined != null && combined.Parameters != null && combined.ParametersDestinationQueryResult != null)
                            {
                                SendValue(combined.ParametersDestinationQueryResult, combined.Parameters);
                            }
                            break;
                        }
                    }
                }
            }
        }
        private void ManageControllerFunctionsSetPointsLimitsAndParameters()
        {
            if (ControlFunctionDictionary != null && PlaceHolders != null && RegisteredQueries != null)
            {
                foreach (var kvp in ControlFunctionDictionary)
                {
                    if (kvp.Value.src != null)
                    {
                        Entry? entryContext = null;
                        if (kvp.Value.src.ContextID != Guid.Empty)
                        {
                            entryContext = GetEntry(kvp.Value.src.ContextID);
                        }
                        if (entryContext != null &&
                            entryContext.LiveValues != null &&
                            entryContext.LiveValues.Count > 0 &&
                            TryGetContextFeatures(entryContext, out List<Vocabulary.Schemas.Nouns.Enum> features))
                        {
                            Dictionary<string, ControllerFunctionData> availableDatas = new Dictionary<string, ControllerFunctionData>();
                            ManageParameters(kvp.Value.src, availableDatas);
                            FindAvailableDatas(availableDatas, kvp.Value.src.controllerDatas, kvp.Value.src);
                            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)> withOnlyLimits;
                            ControllerFunctionData? chosenControllerFunction = ChooseControllerFunction(availableDatas, features, out withOnlyLimits);
                            if (chosenControllerFunction != null)
                            {
                                if (chosenControllerFunction.Parameters != null && chosenControllerFunction.ParametersDestinationQueryResult != null)
                                {
                                    SendValue(chosenControllerFunction.ParametersDestinationQueryResult, chosenControllerFunction.Parameters);
                                }
                                SendControllerFunctionOutputs(chosenControllerFunction, withOnlyLimits, kvp.Key);
                            }
                            break;
                        }
                    }
                }
            }
        }

        private bool SendValue(QueryResult? queryResult, double? value)
        {
            bool ok = false;
            if (_DWISClient != null && queryResult != null && queryResult != null && queryResult.Count > 0 && queryResult[0].Count > 0 && value != null)
            {
                NodeIdentifier id = queryResult[0][0];
                if (id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                {
                    // OPC-UA code to set the value at the node id = ID
                    (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                    outputs[0].nameSpace = id.NameSpace;
                    outputs[0].id = id.ID;
                    outputs[0].value = value.Value;
                    outputs[0].sourceTimestamp = DateTime.UtcNow;
                    ok = _DWISClient.UpdateAnyVariables(outputs);
                    if (ok)
                    {
                        _logger?.LogInformation("sent value: " + value.Value);
                    }
                }
            }
            return ok;
        }
        private bool SendValue(QueryResult? queryResult, object? value)
        {
            bool ok = false;
            if (_DWISClient != null && queryResult != null && queryResult != null && queryResult.Count > 0 && queryResult[0].Count > 0 && value != null)
            {
                string? json = null;
                try
                {
                    json = JsonConvert.SerializeObject(value);
                }
                catch (Exception e)
                {
                    _logger?.LogError(e.ToString());
                }
                NodeIdentifier id = queryResult[0][0];
                if (!string.IsNullOrEmpty(json) && id != null && !string.IsNullOrEmpty(id.ID) && !string.IsNullOrEmpty(id.NameSpace))
                {
                    // OPC-UA code to set the value at the node id = ID
                    (string nameSpace, string id, object value, DateTime sourceTimestamp)[] outputs = new (string nameSpace, string id, object value, DateTime sourceTimestamp)[1];
                    outputs[0].nameSpace = id.NameSpace;
                    outputs[0].id = id.ID;
                    outputs[0].value = json;
                    outputs[0].sourceTimestamp = DateTime.UtcNow;
                    ok = _DWISClient.UpdateAnyVariables(outputs);
                    if (ok)
                    {
                        _logger?.LogInformation("sent value: json serialized");
                    }
                }
            }
            return ok;
        }
        private void ProcessRateOfChange(ControllerFunctionData? chosenControllerFunctionData, Guid guid)
        {
            if (chosenControllerFunctionData != null && ControlFunctionDictionary != null && ControlFunctionDictionary.ContainsKey(guid))
            {
                var v = ControlFunctionDictionary[guid];
                DateTime now = DateTime.UtcNow;
                if (v.last != null && v.lastTimeStamp > DateTime.MinValue)
                {
                    double deltat = (now - v.lastTimeStamp).TotalSeconds;
                    if (chosenControllerFunctionData.ControllerDatas != null && v.last.ControllerDatas != null && chosenControllerFunctionData.ControllerDatas.Count == v.last.ControllerDatas.Count)
                    {
                        for (int i = 0; i < chosenControllerFunctionData.ControllerDatas.Count; i++)
                        {
                            var c1 = chosenControllerFunctionData.ControllerDatas[i];
                            var c0 = v.last.ControllerDatas[i];
                            if (c0 != null && c1 != null)
                            {
                                if (c0.SetPointRecommendation != null && c1.SetPointRecommendation != null && c1.SetPointRateOfChange != null)
                                {
                                    double xlr = c1.SetPointRecommendation.Value;
                                    double xlci = c0.SetPointRecommendation.Value;
                                    double x_dot = c1.SetPointRateOfChange.Value;
                                    double xlci1 = xlr;
                                    if (!Numeric.EQ(xlr, xlci) && !Numeric.EQ(deltat, 0)) 
                                    {
                                        double sgn = (xlr - xlci) / Math.Abs(xlr - xlci);
                                        xlci1 = xlci + sgn * Math.Min(Math.Abs(x_dot), Math.Abs(xlr - xlci) / deltat) * deltat;
                                    }
                                    c1.SetPointRecommendation = xlci1;
                                }
                                if (c0.ControllerLimitDatas != null && c1.ControllerLimitDatas != null && c0.ControllerLimitDatas.Count == c1.ControllerLimitDatas.Count)
                                {
                                    for (int j = 0; j < c0.ControllerLimitDatas.Count; j++)
                                    {
                                        var l1 = c1.ControllerLimitDatas[j];
                                        var l0 = c0.ControllerLimitDatas[j];
                                        if (l0 != null && l1 != null && l0.LimitRecommendation != null && l1.LimitRecommendation != null && l1.LimitRateOfChange != null) 
                                        {
                                            double xlr = l1.LimitRecommendation.Value;
                                            double xlci = l0.LimitRecommendation.Value;
                                            double x_dot = l1.LimitRateOfChange.Value;
                                            double xlci1 = xlr;
                                            if (!Numeric.EQ(xlr, xlci) && !Numeric.EQ(deltat, 0))
                                            {
                                                double sgn = (xlr - xlci) / Math.Abs(xlr - xlci);
                                                xlci1 = xlci + sgn * Math.Min(Math.Abs(x_dot), Math.Abs(xlr - xlci) / deltat) * deltat;
                                            }
                                            l1.LimitRecommendation = xlci1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                ControlFunctionDictionary[guid] = new(v.src, chosenControllerFunctionData, now);
            }
        }
        private Entry? GetEntry(Guid ID)
        {
            Entry? entry = null;
            if (RegisteredQueries != null)
            {
                foreach (var kvp in RegisteredQueries)
                {
                    if (kvp.Value != null && kvp.Value.Key == ID)
                    {
                        entry = kvp.Value;
                        break;
                    }
                }
            }
            return entry;
        }

        private QueryResult? GetQueryResult(Guid ID)
        {
            QueryResult? queryResult = null;
            if (PlaceHolders != null)
            {
                if (PlaceHolders.ContainsKey(ID))
                {
                    queryResult = PlaceHolders[ID];
                }
            }
            return queryResult;
        }
        private void SelectProcedureFunctionData(Dictionary<string, ProcedureData> dict, List<Vocabulary.Schemas.Nouns.Enum> features, List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ProcedureData data)>? possibilities)
        {
            if (dict != null && features != null && possibilities != null)
            {
                List<(List<Vocabulary.Schemas.Nouns.Enum> features, ProcedureData data)> results = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, ProcedureData data)>();
                foreach (var kvp in dict)
                {
                    if (kvp.Value != null && kvp.Value.Features != null)
                    {
                        ProcedureData procedureData = kvp.Value;
                        List<Vocabulary.Schemas.Nouns.Enum> intersect = kvp.Value.Features.Intersect(features).ToList();
                        if (features.Count == 0 || (intersect != null && intersect.Count > 0))
                        {
                            results.Add((intersect, procedureData));
                        }
                    }
                }
                if (results.Count > 0)
                {
                    foreach (var res in results)
                    {
                        possibilities.Add(new(res.features, 0, res.data));
                        possibilities = possibilities.OrderByDescending(x => x.features.Count).ToList();
                    }
                }
            }
        }
        private void SelectControllerFunctionData(Dictionary<string, ControllerFunctionData> dict, List<Vocabulary.Schemas.Nouns.Enum> features, List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)>? onlyLimits, List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)>? withSetPoints)
        {
            if (dict != null && features != null && onlyLimits != null && withSetPoints != null)
            {
                List<(List<Vocabulary.Schemas.Nouns.Enum> features, ControllerFunctionData data)> results = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, ControllerFunctionData data)>();
                foreach (var kvp in dict)
                {
                    if (kvp.Value != null && kvp.Value.Features != null)
                    {
                        ControllerFunctionData controllerFunctionData = kvp.Value;
                        List<Vocabulary.Schemas.Nouns.Enum> intersect = kvp.Value.Features.Intersect(features).ToList();
                        if (features.Count == 0 || (intersect != null && intersect.Count > 0))
                        {
                            results.Add((intersect, controllerFunctionData));
                        }
                    }
                }
                if (results.Count > 0)
                {
                    foreach (var res in results)
                    {
                        int numberOfSetPoints = GetNumberOfSetPoints(res.data);
                        if (numberOfSetPoints > 0)
                        {
                            withSetPoints.Add(new(res.features, numberOfSetPoints, res.data));
                            withSetPoints = withSetPoints.OrderByDescending(x => x.features.Count).ToList();
                        }
                        else
                        {
                            onlyLimits.Add(new (res.features, 0, res.data));
                            onlyLimits = onlyLimits.OrderByDescending(x => x.features.Count).ToList();
                        }
                    }
                }
            }
        }
        private int GetNumberOfSetPoints(ControllerFunctionData cfd)
        {
            int count = 0;
            if (cfd != null)
            {
                foreach (var c in cfd.ControllerDatas)
                {
                    if (c != null && c.SetPointRecommendation != null)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private void MergeLimits(ControllerFunctionData dest, ControllerFunctionData src)
        {
            if (dest != null && src != null)
            {
                if (dest.ControllerDatas != null && src.ControllerDatas != null && dest.ControllerDatas.Count == src.ControllerDatas.Count)
                {
                    for (int i = 0; i < dest.ControllerDatas.Count; i++)
                    {
                        if (dest.ControllerDatas[i].ControllerLimitDatas != null && src.ControllerDatas[i].ControllerLimitDatas != null && src.ControllerDatas[i].ControllerLimitDatas.Count == dest.ControllerDatas[i].ControllerLimitDatas.Count)
                        {
                            for (int j = 0; j < dest.ControllerDatas[i].ControllerLimitDatas.Count; j++)
                            {
                                if (dest.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation == null)
                                {
                                    dest.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation = src.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation;
                                }
                                else
                                {
                                    if (src.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation != null)
                                    if (src.ControllerDatas[i].ControllerLimitDatas[j].IsMin) 
                                    {
                                        dest.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation = Math.Max(dest.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation!.Value, src.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation!.Value);
                                    }
                                    else
                                    {
                                        dest.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation = Math.Min(dest.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation!.Value, src.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation!.Value);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        private bool ControlFunctionAlreadyRegistered(string activableFunctionName)
        {
            bool found = false;
            foreach (var kvp1 in ControlFunctionDictionary)
            {
                if (kvp1.Value.src != null && kvp1.Value.src.ControllerFunction != null && !string.IsNullOrEmpty(kvp1.Value.src.ControllerFunction.Name) && kvp1.Value.src.ControllerFunction.Name.Equals(activableFunctionName))
                {
                    found = true;
                    break;
                }
            }
            return found;
        }
        private void RegisterControllerContext(ControllerFunction controllerFunction, ControlFunctionData controlFunctionData)
        {
            if (controllerFunction.ContextSemanticInfo != null)
            {
                if (string.IsNullOrEmpty(controllerFunction.ContextSemanticInfo.SparQLQuery) || controllerFunction.ContextSemanticInfo.SparQLVariables == null)
                {
                    controllerFunction.FillInSparqlQueriesAndManifests();
                }
                controlFunctionData.ContextID = Guid.NewGuid();
                RegisterQuery(controllerFunction.ContextSemanticInfo, RegisteredQueries, controlFunctionData.ContextID);
            }
        }
        private void RegisterControllerParameters(ControllerFunction controllerFunction, ControlFunctionData controlFunctionData)
        {
            if (controllerFunction.Parameters != null)
            {
                if (string.IsNullOrEmpty(controllerFunction.Parameters.SparQLAlternateQuery) || controllerFunction.Parameters.SparQLAlternateVariables == null)
                {
                    controllerFunction.FillInSparqlQueriesAndManifests();
                }
                controlFunctionData.ParametersID = Guid.NewGuid();
                RegisterQueryAlternate(controllerFunction.Parameters, RegisteredQueries, controlFunctionData.ParametersID);
            }
            if (controllerFunction.Parameters != null &&
                !string.IsNullOrEmpty(controllerFunction.Parameters.SparQLQuery))
            {
                if (string.IsNullOrEmpty(controllerFunction.Parameters.SparQLQuery) || controllerFunction.Parameters.SparQLVariables == null)
                {
                    controllerFunction.FillInSparqlQueriesAndManifests();
                }
                QueryResult? result = null;
                RegisterToBlackboard(controllerFunction.Parameters, _DWISClient, ref result);
                if (result != null)
                {
                    controlFunctionData.ParametersDestinationID = Guid.NewGuid();
                    PlaceHolders.Add(controlFunctionData.ParametersDestinationID, result);
                }
            }
        }
        private void RegisterControllers(ControllerFunction controllerFunction, ControlFunctionData controlFunctionData)
        {
            if (controllerFunction.Controllers != null)
            {
                foreach (Controller controller in controllerFunction.Controllers)
                {
                    if (controller != null)
                    {
                        ControlData controllerData = new ControlData();
                        controlFunctionData.controllerDatas.Add(controllerData);
                        RegisterControllerParameters(controller, controllerData);
                        RegisterControllerSetPoints(controller, controllerData);
                        RegisterControllerLimits(controller, controllerData);
                    }
                }
            }
        }
        private void RegisterControllerParameters(Controller controller, ControlData controllerData)
        {
            if (controller.Parameters != null)
            {
                controllerData.ParametersID = Guid.NewGuid();
                RegisterQueryAlternate(controller.Parameters, RegisteredQueries, controllerData.ParametersID);
            }
            if (controller.Parameters != null &&
                !string.IsNullOrEmpty(controller.Parameters.SparQLQuery) &&
                controller.Parameters.SparQLVariables != null &&
                controller.Parameters.SparQLVariables.Count > 0)
            {
                QueryResult? result = null;
                RegisterToBlackboard(controller.Parameters, _DWISClient, ref result);
                if (result != null)
                {
                    controllerData.ParametersDestinationID = Guid.NewGuid();
                    PlaceHolders.Add(controllerData.ParametersDestinationID, result);
                }
            }
        }
        private void RegisterControllerSetPoints(Controller controller, ControlData controllerData)
        {
            if (controller is ControllerWithControlledVariable controllerWithControlledVariable)
            {
                if (controllerWithControlledVariable.SetPointReference != null &&
                    !string.IsNullOrEmpty(controllerWithControlledVariable.SetPointReference.SparQLAlternateQuery) &&
                    controllerWithControlledVariable.SetPointReference.SparQLAlternateVariables != null &&
                    controllerWithControlledVariable.SetPointReference.SparQLAlternateVariables.Count > 0)
                {
                    controllerData.SetPointSourceID = Guid.NewGuid();
                    RegisterQueryAlternate(controllerWithControlledVariable.SetPointReference, RegisteredQueries, controllerData.SetPointSourceID);
                }
                if (controllerWithControlledVariable.SetPointReference != null &&
                    !string.IsNullOrEmpty(controllerWithControlledVariable.SetPointReference.SparQLQuery) &&
                    controllerWithControlledVariable.SetPointReference.SparQLVariables != null &&
                    controllerWithControlledVariable.SetPointReference.SparQLVariables.Count > 0)
                {
                    QueryResult? result = null;
                    RegisterToBlackboard(controllerWithControlledVariable.SetPointReference, _DWISClient, ref result);
                    if (result != null)
                    {
                        controllerData.SetPointDestinationID = Guid.NewGuid();
                        PlaceHolders.Add(controllerData.SetPointDestinationID, result);
                    }
                }
                if (controllerWithControlledVariable.ControlledVariableReference != null &&
                    !string.IsNullOrEmpty(controllerWithControlledVariable.ControlledVariableReference.SparQLQuery) &&
                    controllerWithControlledVariable.ControlledVariableReference.SparQLVariables != null &&
                    controllerWithControlledVariable.ControlledVariableReference.SparQLVariables.Count > 0)
                {
                    controllerData.MeasuredValueID = Guid.NewGuid();
                    RegisterQuery(controllerWithControlledVariable.ControlledVariableReference, RegisteredQueries, controllerData.MeasuredValueID);
                }
                if (controllerWithControlledVariable.SetPointMaxRateOfChange != null &&
                    !string.IsNullOrEmpty(controllerWithControlledVariable.SetPointMaxRateOfChange.SparQLQuery) &&
                    controllerWithControlledVariable.SetPointMaxRateOfChange.SparQLVariables != null &&
                    controllerWithControlledVariable.SetPointMaxRateOfChange.SparQLVariables.Count > 0)
                {
                    controllerData.MaxRateOfChangeID = Guid.NewGuid();
                    RegisterQuery(controllerWithControlledVariable.SetPointMaxRateOfChange, RegisteredQueries, controllerData.MaxRateOfChangeID);
                }
            }
        }
        private void RegisterControllerLimits(Controller controller, ControlData controllerData)
        {
            if (controller is ControllerWithOnlyLimits controllerWithOnlyLimits)
            {
                if (controllerWithOnlyLimits.ControllerLimits != null)
                {
                    ManageLimits(controllerWithOnlyLimits.ControllerLimits, controllerData);
                }
            }
            else if (controller is ControllerWithLimits controllerWithLimits)
            {
                ManageLimits(controllerWithLimits.ControllerLimits, controllerData);
            }
        }
        private bool TryGetContextFeatures(Entry entryContext, out List<Vocabulary.Schemas.Nouns.Enum> features)
        {
            features = new List<Vocabulary.Schemas.Nouns.Enum>();
            if (entryContext != null && entryContext.LiveValues != null && entryContext.LiveValues.Count > 0)
            {
                foreach (var kvp2 in entryContext.LiveValues)
                {
                    if (kvp2.Value != null && kvp2.Value.val != null && kvp2.Value.val is string json)
                    {
                        try
                        {
                            DWISContext? context = JsonConvert.DeserializeObject<DWISContext>(json, _contextSerializerSettings);
                            if (context != null)
                            {
                                if (context.CapabilityPreferences != null)
                                {
                                    foreach (var feature in context.CapabilityPreferences)
                                    {
                                        features.Add(feature);
                                    }
                                }
                                return true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex.ToString());
                        }
                    }
                }
            }
            return false;
        }
        private ProcedureData? ChooseProcedureFunction(Dictionary<string, ProcedureData> availableDatas, List<Vocabulary.Schemas.Nouns.Enum> features)
        {
            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ProcedureData data)> possibilities = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ProcedureData data)>();
            SelectProcedureFunctionData(availableDatas, features, possibilities);
            if (possibilities.Count > 0)
            {
                List<Vocabulary.Schemas.Nouns.Enum> fs = possibilities[0].features;
                if (fs != null)
                {
                    List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ProcedureData data)> withSameFeatures = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ProcedureData data)>();
                    foreach (var wol in possibilities)
                    {
                        if (wol.features != null)
                        {
                            bool eq = wol.features.Count == fs.Count;
                            if (eq)
                            {
                                foreach (var f in fs)
                                {
                                    eq &= wol.features.Contains(f);
                                    if (!eq)
                                    {
                                        break;
                                    }
                                }
                            }
                            if (eq)
                            {
                                withSameFeatures.Add(wol);
                            }
                        }
                    }
                    if (withSameFeatures.Count > 0)
                    {
                        withSameFeatures = withSameFeatures.OrderByDescending(wol => wol.Item2).ToList();
                        return withSameFeatures[0].data;
                    }
                }
                return possibilities[0].data;
            }
            return null;
        }
        private SafeOperatingEnvelopeData? CombineSafeOperatingEnvelopes(List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, SafeOperatingEnvelopeData data)> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, SafeOperatingEnvelopeData data)> ordered = candidates.OrderByDescending(c => c.Item2).ToList();
            while (ordered.Count > 0)
            {
                var intersection = TryIntersectEnvelopes(ordered.Select(c => c.data).ToList(), out object? parameter);
                if (intersection && parameter != null)
                {
                    SafeOperatingEnvelopeData result = new SafeOperatingEnvelopeData();
                    result.AdvisorName = ordered[0].data.AdvisorName;
                    result.ParametersDestinationQueryResult = ordered[0].data.ParametersDestinationQueryResult;
                    result.Parameters = parameter;
                    return result;
                }
                ordered.RemoveAt(ordered.Count - 1);
            }
            return null;
        }
        private FaultDetectionIsolationAndRecoveryData? ChooseFaultDetectionIsolationAndRecoveryFunction(Dictionary<string, FaultDetectionIsolationAndRecoveryData> availableDatas, List<Vocabulary.Schemas.Nouns.Enum> features)
        {
            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, FaultDetectionIsolationAndRecoveryData data)> possibilities = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, FaultDetectionIsolationAndRecoveryData data)>();
            SelectFaultDetectionIsolationAndRecoveryFunctionData(availableDatas, features, possibilities);
            if (possibilities.Count > 0)
            {
                List<Vocabulary.Schemas.Nouns.Enum> fs = possibilities[0].features;
                if (fs != null)
                {
                    List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, FaultDetectionIsolationAndRecoveryData data)> withSameFeatures = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, FaultDetectionIsolationAndRecoveryData data)>();
                    foreach (var wol in possibilities)
                    {
                        if (wol.features != null)
                        {
                            bool eq = wol.features.Count == fs.Count;
                            if (eq)
                            {
                                foreach (var f in fs)
                                {
                                    eq &= wol.features.Contains(f);
                                    if (!eq)
                                    {
                                        break;
                                    }
                                }
                            }
                            if (eq)
                            {
                                withSameFeatures.Add(wol);
                            }
                        }
                    }
                    if (withSameFeatures.Count > 0)
                    {
                        withSameFeatures = withSameFeatures.OrderByDescending(wol => wol.Item2).ToList();
                        return withSameFeatures[0].data;
                    }
                }
                return possibilities[0].data;
            }
            return null;
        }
        private ControllerFunctionData? ChooseControllerFunction(Dictionary<string, ControllerFunctionData> availableDatas, List<Vocabulary.Schemas.Nouns.Enum> features, out List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)> withOnlyLimits)
        {
            withOnlyLimits = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)>();
            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)> withSetPoints = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)>();
            SelectControllerFunctionData(availableDatas, features, withOnlyLimits, withSetPoints);
            ControllerFunctionData? chosenControllerFunction = null;
            if (withSetPoints.Count > 0)
            {
                List<Vocabulary.Schemas.Nouns.Enum> fs = withSetPoints[0].features;
                if (fs != null)
                {
                    List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)> withSameFeatures = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)>();
                    foreach (var wol in withSetPoints)
                    {
                        if (wol.features != null)
                        {
                            bool eq = wol.features.Count == fs.Count;
                            if (eq)
                            {
                                foreach (var f in fs)
                                {
                                    eq &= wol.features.Contains(f);
                                    if (!eq)
                                    {
                                        break;
                                    }
                                }
                            }
                            if (eq)
                            {
                                withSameFeatures.Add(wol);
                            }
                        }
                    }
                    if (withSameFeatures.Count > 0)
                    {
                        withSameFeatures = withSameFeatures.OrderByDescending(wol => wol.Item2).ToList();
                        chosenControllerFunction = withSameFeatures[0].data;
                    }
                }
                if (chosenControllerFunction == null)
                {
                    chosenControllerFunction = withSetPoints[0].data;
                }
            }
            if (withOnlyLimits.Count > 0 && chosenControllerFunction == null)
            {
                chosenControllerFunction = withOnlyLimits[0].data;
            }
            return chosenControllerFunction;
        }
        private bool TryIntersectEnvelopes(List<SafeOperatingEnvelopeData> sources, out object? parameter)
        {
            parameter = null;
            if (sources == null || sources.Count == 0)
            {
                return false;
            }
            if (sources.Any(s => s.Parameters == null || s.ParametersDestinationQueryResult == null))
            {
                return false;
            }
            Type paramType = sources[0].Parameters!.GetType();
            if (sources.Any(s => s.Parameters!.GetType() != paramType))
            {
                return false;
            }
            object? cloned = null;
            try
            {
                string json = JsonConvert.SerializeObject(sources[0].Parameters);
                cloned = JsonConvert.DeserializeObject(json, paramType);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.ToString());
                return false;
            }
            if (cloned == null)
            {
                return false;
            }
            var lookupTableProps = paramType.GetProperties().Where(p => p.GetCustomAttributesData().Any(ca => ca.AttributeType.Name.Contains("SOELookupTableAttribute"))).ToList();
            foreach (var prop in lookupTableProps)
            {
                var attrData = prop.GetCustomAttributesData().First(ca => ca.AttributeType.Name.Contains("SOELookupTableAttribute"));
                bool isUpperBound = attrData.ConstructorArguments != null && attrData.ConstructorArguments.Count > 0 && attrData.ConstructorArguments[0].Value != null && attrData.ConstructorArguments[0].Value.ToString() != null && attrData.ConstructorArguments[0].Value.ToString()!.Contains("Upper");
                List<object> tables = new List<object>();
                foreach (var s in sources)
                {
                    var val = prop.GetValue(s.Parameters!);
                    if (val != null)
                    {
                        tables.Add(val);
                    }
                }
                if (tables.Count == 0)
                {
                    return false;
                }
                var intersected = IntersectLookupTables(tables, isUpperBound);
                if (intersected == null)
                {
                    return false;
                }
                prop.SetValue(cloned, intersected);
            }
            parameter = cloned;
            return true;
        }
        private object? IntersectLookupTables(List<object> tables, bool isUpperBound)
        {
            if (tables == null || tables.Count == 0)
            {
                return null;
            }
            Type tableType = tables[0].GetType();
            var axisProps = tableType.GetProperties().Select(p => new { Prop = p, Attr = p.GetCustomAttributesData().FirstOrDefault(ca => ca.AttributeType.Name.Contains("SOELookupTableAxisAttribute")) }).Where(x => x.Attr != null).ToList();
            axisProps = axisProps.OrderBy(x => x.Attr!.ConstructorArguments != null && x.Attr!.ConstructorArguments.Count > 0 ? Convert.ToInt32(x.Attr!.ConstructorArguments[0].Value) : 0).ToList();
            var valueProp = tableType.GetProperties().FirstOrDefault(p => p.GetCustomAttributesData().Any(ca => ca.AttributeType.Name.Contains("SOELookupTableValueAttribute")));
            if (axisProps.Count == 0 || valueProp == null)
            {
                return null;
            }
            List<double[]> intersectedAxes = new List<double[]>();
            foreach (var axis in axisProps)
            {
                double globalMin = double.NegativeInfinity;
                double globalMax = double.PositiveInfinity;
                List<double> collected = new List<double>();
                foreach (var t in tables)
                {
                    var axisVals = axis.Prop.GetValue(t) as System.Array;
                    if (axisVals != null && axisVals.Length > 0)
                    {
                        List<double> vals = new List<double>();
                        foreach (var v in axisVals)
                        {
                            if (v is IConvertible)
                            {
                                vals.Add(Convert.ToDouble(v));
                            }
                        }
                        if (vals.Count > 0)
                        {
                            vals.Sort();
                            double min = vals.First();
                            double max = vals.Last();
                            globalMin = Math.Max(globalMin, min);
                            globalMax = Math.Min(globalMax, max);
                            collected.AddRange(vals.Where(v => v >= globalMin && v <= globalMax));
                        }
                    }
                }
                var axisValues = collected.Where(v => v >= globalMin && v <= globalMax).Distinct().OrderBy(v => v).ToArray();
                if (axisValues.Length == 0 || globalMin > globalMax)
                {
                    return null;
                }
                intersectedAxes.Add(axisValues);
            }
            int[] lengths = intersectedAxes.Select(a => a.Length).ToArray();
            Type? elementType = valueProp.PropertyType.GetElementType();
            if (elementType == null)
            {
                return null;
            }
            var newValues = System.Array.CreateInstance(elementType, lengths);
            MethodInfo? interpolate = tableType.GetMethod("Interpolate");
            if (interpolate == null)
            {
                return null;
            }
            bool anyValue = false;
            void RecurseFill(int dim, int[] indices)
            {
                if (dim == intersectedAxes.Count)
                {
                    double? combined = null;
                    foreach (var t in tables)
                    {
                        double[] coords = new double[intersectedAxes.Count];
                        for (int i = 0; i < intersectedAxes.Count; i++)
                        {
                            coords[i] = intersectedAxes[i][indices[i]];
                        }
                        object? result = interpolate.Invoke(t, coords.Cast<object>().ToArray());
                        if (result != null)
                        {
                            double val = Convert.ToDouble(result);
                            if (combined == null)
                            {
                                combined = val;
                            }
                            else
                            {
                                combined = isUpperBound ? Math.Min(combined.Value, val) : Math.Max(combined.Value, val);
                            }
                        }
                    }
                    if (combined != null)
                    {
                        anyValue = true;
                        newValues.SetValue(Convert.ChangeType(combined.Value, elementType), indices);
                    }
                    return;
                }
                for (int i = 0; i < intersectedAxes[dim].Length; i++)
                {
                    indices[dim] = i;
                    RecurseFill(dim + 1, indices);
                }
            }
            RecurseFill(0, new int[intersectedAxes.Count]);
            if (!anyValue)
            {
                return null;
            }
            object? newTable = Activator.CreateInstance(tableType);
            if (newTable == null)
            {
                return null;
            }
            for (int i = 0; i < axisProps.Count; i++)
            {
                axisProps[i].Prop.SetValue(newTable, intersectedAxes[i]);
            }
            valueProp.SetValue(newTable, newValues);
            return newTable;
        }
        private void SendControllerFunctionOutputs(ControllerFunctionData chosenControllerFunction, List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)> withOnlyLimits, Guid guid)
        {
            foreach (var wol in withOnlyLimits)
            {
                if (wol.data != null)
                {
                    MergeLimits(chosenControllerFunction, wol.data);
                }
            }
            ProcessRateOfChange(chosenControllerFunction, guid);
            foreach (var c in chosenControllerFunction.ControllerDatas)
            {
                if (c != null)
                {
                    if (c.ParametersDestinationQueryResult != null && c.Parameters != null)
                    {
                        SendValue(c.ParametersDestinationQueryResult, c.Parameters);
                    }
                    if (c.SetPointDestinationQueryResult != null && c.SetPointRecommendation != null)
                    {
                        SendValue(c.SetPointDestinationQueryResult, c.SetPointRecommendation);
                    }
                    if (c.ControllerLimitDatas != null)
                    {
                        foreach (var l in c.ControllerLimitDatas)
                        {
                            if (l != null && l.LimitDestinationQueryResult != null && l.LimitRecommendation != null)
                            {
                                SendValue(l.LimitDestinationQueryResult, l.LimitRecommendation);
                            }
                        }
                    }
                }
            }
        }
        private void AddEmpty(Dictionary<string, FaultDetectionIsolationAndRecoveryData> dict, string advisorName, FaultDetectionIsolationAndRecoveryFunctionData sample)
        {
            if (dict != null && !string.IsNullOrEmpty(advisorName) && sample != null)
            {
                FaultDetectionIsolationAndRecoveryData empty = new FaultDetectionIsolationAndRecoveryData();
                dict.Add(advisorName, empty);
                empty.AdvisorName = advisorName;
                if (empty.Features == null)
                {
                    empty.Features = new List<Vocabulary.Schemas.Nouns.Enum>();
                }
            }
        }
        private void AddEmpty(Dictionary<string, SafeOperatingEnvelopeData> dict, string advisorName, SafeOperatingEnvelopeFunctionData sample)
        {
            if (dict != null && !string.IsNullOrEmpty(advisorName) && sample != null)
            {
                SafeOperatingEnvelopeData empty = new SafeOperatingEnvelopeData();
                dict.Add(advisorName, empty);
                empty.AdvisorName = advisorName;
                if (empty.Features == null)
                {
                    empty.Features = new List<Vocabulary.Schemas.Nouns.Enum>();
                }
            }
        }
        private void AddEmpty(Dictionary<string, ProcedureData> dict, string advisorName, ProcedureFunctionData sample)
        {
            if (dict != null && !string.IsNullOrEmpty(advisorName) && sample != null)
            {
                ProcedureData empty = new ProcedureData();
                dict.Add(advisorName, empty);
                empty.AdvisorName = advisorName;
                if (empty.Features == null)
                {
                    empty.Features = new List<Vocabulary.Schemas.Nouns.Enum>();
                }
            }
        }
        private void AddEmpty(Dictionary<string, ControllerFunctionData> dict, string advisorName, ControlFunctionData sample)
        {
            if (dict != null && !string.IsNullOrEmpty(advisorName) && sample != null)
            {
                ControllerFunctionData empty = new ControllerFunctionData();
                dict.Add(advisorName, empty);
                empty.AdvisorName = advisorName;
                if (empty.Features == null)
                {
                    empty.Features = new List<Vocabulary.Schemas.Nouns.Enum>();
                }
                if (empty.ControllerDatas == null) { }
                {
                    empty.ControllerDatas = new List<ControllerData>();
                }
                if (sample.controllerDatas != null)
                {
                    foreach (var v in sample.controllerDatas)
                    {
                        if (v != null)
                        {
                            var cd = new ControllerData();
                            empty.ControllerDatas.Add(cd);
                            if (v.LimitIDs != null)
                            {
                                if (cd.ControllerLimitDatas == null)
                                {
                                    cd.ControllerLimitDatas = new List<ControllerLimitData>();
                                }
                                foreach (var id in v.LimitIDs)
                                {
                                    cd.ControllerLimitDatas.Add(new ControllerLimitData() { IsMin = id.IsMin });
                                }
                            }
                        }
                    }
                }
            }
        }

        private object? SearchValue(Dictionary<Guid, LiveValue> liveValues, NodeIdentifier ID)
        {
            object? val = null;
            if (liveValues != null && ID != null && !string.IsNullOrEmpty(ID.ID) && !string.IsNullOrEmpty(ID.NameSpace))
            {
                foreach (var kvp in liveValues)
                {
                    if (kvp.Value != null && ID.ID.Equals(kvp.Value.id) && ID.NameSpace.Equals(kvp.Value.ns))
                    {
                        val = kvp.Value.val;
                        break;
                    }
                }
            }
            return val;
        }
        private void AddFeature(ProcedureData? cf, Vocabulary.Schemas.Nouns.Enum f)
        {
            if (cf != null && cf.Features != null && !cf.Features.Contains(f))
            {
                cf.Features.Add(f);
            }
        }
        private void AddFeature(FaultDetectionIsolationAndRecoveryData? cf, Vocabulary.Schemas.Nouns.Enum f)
        {
            if (cf != null && cf.Features != null && !cf.Features.Contains(f))
            {
                cf.Features.Add(f);
            }
        }
        private void AddFeature(SafeOperatingEnvelopeData? cf, Vocabulary.Schemas.Nouns.Enum f)
        {
            if (cf != null && cf.Features != null && !cf.Features.Contains(f))
            {
                cf.Features.Add(f);
            }
        }
        private void AddFeature(ControllerFunctionData? cf, Vocabulary.Schemas.Nouns.Enum f)
        {
            if (cf != null && cf.Features != null && !cf.Features.Contains(f))
            {
                cf.Features.Add(f);
            }
        }
        private void FindAvailableDatas(Dictionary<string, ControllerFunctionData> availableDatas, List<ControlData> controlDatas, ControlFunctionData controlFunctionData)
        {
            for (int i = 0; i < controlDatas.Count; i++)
            {
                var controllerData = controlDatas[i];
                if (controllerData != null)
                {
                    if (controllerData.ParametersID != Guid.Empty)
                    {
                        ManageParameters(controllerData, controlFunctionData, i, availableDatas);
                    }
                    if (controllerData.SetPointSourceID != Guid.Empty &&
                        controllerData.SetPointDestinationID != Guid.Empty &&
                        controllerData.MeasuredValueID != Guid.Empty &&
                        controllerData.MaxRateOfChangeID != Guid.Empty)
                    {
                        ManageSetPoint(controllerData, controlFunctionData, i, availableDatas);
                    }
                    if (controllerData.LimitIDs != null)
                    {
                        for (int j = 0; j < controllerData.LimitIDs.Count; j++)
                        {
                            var v = controllerData.LimitIDs[j];
                            if (v != null &&
                                v.MaxLimitSourceID != Guid.Empty &&
                                v.MaxLimitDestinationID != Guid.Empty &&
                                v.MaxRateOfChangeID != Guid.Empty)
                            {
                                ManageLimit(v, controlFunctionData, i, j, availableDatas);
                            }
                        }
                    }
                }
            }
        }
        private void ManageParameters(ProcedureFunctionData procedureFunctionData, Dictionary<string, ProcedureData> availableDatas)
        {
            Entry? parametersSource = GetEntry(procedureFunctionData.ParametersID);
            QueryResult? parametersDestination = GetQueryResult(procedureFunctionData.ParametersDestinationID);
            if (parametersSource != null &&
                parametersSource.Results != null &&
                parametersSource.LiveValues != null &&
                parametersDestination != null)
            {
                foreach (var res in parametersSource.Results)
                {
                    if (res != null && res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, procedureFunctionData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null)
                        {
                            object? val = SearchValue(parametersSource.LiveValues, res[0]);
                            if (val != null)
                            {
                                cf.Parameters = val;
                                cf.ParametersDestinationQueryResult = parametersDestination;
                            }
                        }
                    }
                }
            }
        }
        private void SelectSafeOperatingEnvelopeFunctionData(Dictionary<string, SafeOperatingEnvelopeData> dict, List<Vocabulary.Schemas.Nouns.Enum> features, List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, SafeOperatingEnvelopeData data)>? possibilities)
        {
            if (dict != null && features != null && possibilities != null)
            {
                List<(List<Vocabulary.Schemas.Nouns.Enum> features, SafeOperatingEnvelopeData data)> results = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, SafeOperatingEnvelopeData data)>();
                foreach (var kvp in dict)
                {
                    if (kvp.Value != null && kvp.Value.Features != null)
                    {
                        SafeOperatingEnvelopeData soeData = kvp.Value;
                        List<Vocabulary.Schemas.Nouns.Enum> intersect = kvp.Value.Features.Intersect(features).ToList();
                        if (features.Count == 0 || (intersect != null && intersect.Count > 0))
                        {
                            results.Add((intersect, soeData));
                        }
                    }
                }
                if (results.Count > 0)
                {
                    foreach (var res in results)
                    {
                        possibilities.Add(new(res.features, res.features.Count, res.data));
                        possibilities = possibilities.OrderByDescending(x => x.features.Count).ToList();
                    }
                }
            }
        }
        private void ManageParameters(SafeOperatingEnvelopeFunctionData soeFunctionData, Dictionary<string, SafeOperatingEnvelopeData> availableDatas)
        {
            Entry? parametersSource = GetEntry(soeFunctionData.ParametersID);
            QueryResult? parametersDestination = GetQueryResult(soeFunctionData.ParametersDestinationID);
            if (parametersSource != null &&
                parametersSource.Results != null &&
                parametersSource.LiveValues != null &&
                parametersDestination != null)
            {
                foreach (var res in parametersSource.Results)
                {
                    if (res != null && res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, soeFunctionData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null)
                        {
                            object? val = SearchValue(parametersSource.LiveValues, res[0]);
                            if (val != null)
                            {
                                cf.Parameters = val;
                                cf.ParametersDestinationQueryResult = parametersDestination;
                            }
                        }
                    }
                }
            }
        }
        private void SelectFaultDetectionIsolationAndRecoveryFunctionData(Dictionary<string, FaultDetectionIsolationAndRecoveryData> dict, List<Vocabulary.Schemas.Nouns.Enum> features, List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, FaultDetectionIsolationAndRecoveryData data)>? possibilities)
        {
            if (dict != null && features != null && possibilities != null)
            {
                List<(List<Vocabulary.Schemas.Nouns.Enum> features, FaultDetectionIsolationAndRecoveryData data)> results = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, FaultDetectionIsolationAndRecoveryData data)>();
                foreach (var kvp in dict)
                {
                    if (kvp.Value != null && kvp.Value.Features != null)
                    {
                        FaultDetectionIsolationAndRecoveryData fdirData = kvp.Value;
                        List<Vocabulary.Schemas.Nouns.Enum> intersect = kvp.Value.Features.Intersect(features).ToList();
                        if (features.Count == 0 || (intersect != null && intersect.Count > 0))
                        {
                            results.Add((intersect, fdirData));
                        }
                    }
                }
                if (results.Count > 0)
                {
                    foreach (var res in results)
                    {
                        possibilities.Add(new(res.features, 0, res.data));
                        possibilities = possibilities.OrderByDescending(x => x.features.Count).ToList();
                    }
                }
            }
        }
        private void ManageParameters(FaultDetectionIsolationAndRecoveryFunctionData functionData, Dictionary<string, FaultDetectionIsolationAndRecoveryData> availableDatas)
        {
            Entry? parametersSource = GetEntry(functionData.ParametersID);
            QueryResult? parametersDestination = GetQueryResult(functionData.ParametersDestinationID);
            if (parametersSource != null &&
                parametersSource.Results != null &&
                parametersSource.LiveValues != null &&
                parametersDestination != null)
            {
                foreach (var res in parametersSource.Results)
                {
                    if (res != null && res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, functionData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null)
                        {
                            object? val = SearchValue(parametersSource.LiveValues, res[0]);
                            if (val != null)
                            {
                                cf.Parameters = val;
                                cf.ParametersDestinationQueryResult = parametersDestination;
                            }
                        }
                    }
                }
            }
        }
        private void ManageParameters(ControlFunctionData controllerFunctionData, Dictionary<string, ControllerFunctionData> availableDatas)
        {
            Entry? parametersSource = GetEntry(controllerFunctionData.ParametersID);
            QueryResult? parametersDestination = GetQueryResult(controllerFunctionData.ParametersDestinationID);
            if (parametersSource != null &&
                parametersSource.Results != null &&
                parametersSource.LiveValues != null &&
                parametersDestination != null)
            {
                foreach (var res in parametersSource.Results)
                {
                    if (res != null && res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, controllerFunctionData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null)
                        {
                            object? val = SearchValue(parametersSource.LiveValues, res[0]);
                            if (val != null)
                            {
                                cf.Parameters = val;
                                cf.ParametersDestinationQueryResult = parametersDestination;
                            }
                        }
                    }
                }
            }
        }
        private void ManageParameters(ControlData controllerData, ControlFunctionData refData, int i, Dictionary<string, ControllerFunctionData> availableDatas)
        {
            Entry? parametersSource = GetEntry(controllerData.ParametersID);
            QueryResult? parametersDestination = GetQueryResult(controllerData.ParametersDestinationID);
            if (parametersSource != null &&
                parametersSource.Results != null &&
                parametersSource.LiveValues != null &&
                parametersDestination != null)
            {
                foreach (var res in parametersSource.Results)
                {
                    if (res != null && res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, refData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null && cf.ControllerDatas != null && cf.ControllerDatas.Count > i && cf.ControllerDatas[i] != null)
                        {
                            object? val = SearchValue(parametersSource.LiveValues, res[0]);
                            if (val != null)
                            {
                                cf.ControllerDatas[i].Parameters = val;
                                cf.ControllerDatas[i].ParametersDestinationQueryResult = parametersDestination;
                            }
                        }
                    }
                }
            }
        }
        private void ManageSetPoint(ControlData controllerData, ControlFunctionData refData, int i, Dictionary<string, ControllerFunctionData> availableDatas)
        {
            Entry? setPointSource = GetEntry(controllerData.SetPointSourceID);
            Entry? measuredValue = GetEntry(controllerData.MeasuredValueID);
            Entry? maxRateOfChange = GetEntry(controllerData.MaxRateOfChangeID);
            QueryResult? setPointDestination = GetQueryResult(controllerData.SetPointDestinationID);
            if (setPointSource != null &&
                setPointSource.Results != null &&
                setPointSource.LiveValues != null &&
                measuredValue != null &&
                measuredValue.LiveValues != null &&
                measuredValue.LiveValues.Values != null &&
                measuredValue.LiveValues.Values.Any() &&
                measuredValue.LiveValues.Values.First() != null &&
                measuredValue.LiveValues.Values.First().val != null &&
                measuredValue.LiveValues.Values.First().val is double mVal &&
                maxRateOfChange != null &&
                maxRateOfChange.LiveValues != null &&
                maxRateOfChange.LiveValues.Values != null &&
                maxRateOfChange.LiveValues.Values.Any() &&
                maxRateOfChange.LiveValues.Values.First() != null &&
                maxRateOfChange.LiveValues.Values.First().val != null &&
                maxRateOfChange.LiveValues.Values.First().val is double mROC &&
                setPointDestination != null)
            {
                foreach (var res in setPointSource.Results)
                {
                    if (res != null && res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, refData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null && cf.ControllerDatas != null && cf.ControllerDatas.Count > i && cf.ControllerDatas[i] != null)
                        {
                            object? val = SearchValue(setPointSource.LiveValues, res[0]);
                            if (val != null && val is double dval)
                            {
                                cf.ControllerDatas[i].SetPointRecommendation = dval;
                                cf.ControllerDatas[i].MeasuredValue = mVal;
                                cf.ControllerDatas[i].SetPointRateOfChange = mROC;
                                cf.ControllerDatas[i].SetPointDestinationQueryResult = setPointDestination;
                                /*
                                string features = string.Empty;
                                foreach (var feature in cf.Features)
                                {
                                    features += feature.ToString() + " ";
                                }
                                _logger?.LogInformation("Received Set-point: " + dval + " with features: " + features);
                                */
                            }
                        }
                    }
                }
            }
        }
        private void ManageLimit(LimitData limitData, ControlFunctionData refData, int i, int j, Dictionary<string, ControllerFunctionData> availableDatas)
        {
            Entry? maxLimitSource = GetEntry(limitData.MaxLimitSourceID);
            Entry? maxRateOfChange = GetEntry(limitData.MaxRateOfChangeID);
            QueryResult? maxLimitDestination = GetQueryResult(limitData.MaxLimitDestinationID);
            if (maxLimitSource != null && 
                maxLimitSource.Results != null && 
                maxLimitSource.LiveValues != null &&
                maxRateOfChange != null &&
                maxRateOfChange.LiveValues != null &&
                maxRateOfChange.LiveValues.Values != null &&
                maxRateOfChange.LiveValues.Values.Any() &&
                maxRateOfChange.LiveValues.Values.First() != null &&
                maxRateOfChange.LiveValues.Values.First().val != null &&
                maxRateOfChange.LiveValues.Values.First().val is double mROC &&
                maxLimitDestination != null)
            {
                foreach (var res in maxLimitSource.Results)
                {
                    if (res != null && 
                        res.Count >= 3 &&
                        res[0] != null &&
                        res[1] != null &&
                        res[2] != null &&
                        !string.IsNullOrEmpty(res[0].ID) &&
                        !string.IsNullOrEmpty(res[1].ID) &&
                        !string.IsNullOrEmpty(res[2].ID) &&
                        Enum.TryParse(res[2].ID, out Vocabulary.Schemas.Nouns.Enum f))
                    {
                        if (!availableDatas.ContainsKey(res[1].ID))
                        {
                            AddEmpty(availableDatas, res[1].ID, refData);
                        }
                        var cf = availableDatas[res[1].ID];
                        AddFeature(cf, f);
                        if (cf != null &&
                            cf.ControllerDatas != null && 
                            cf.ControllerDatas.Count > i && 
                            cf.ControllerDatas[i] != null && 
                            cf.ControllerDatas[i].ControllerLimitDatas != null &&
                            cf.ControllerDatas[i].ControllerLimitDatas.Count > j &&
                            cf.ControllerDatas[i].ControllerLimitDatas[j] != null)
                        {
                            object? val = SearchValue(maxLimitSource.LiveValues, res[0]);
                            if (val != null && val is double dval)
                            {
                                cf.ControllerDatas[i].ControllerLimitDatas[j].LimitRecommendation = dval;
                                cf.ControllerDatas[i].ControllerLimitDatas[j].LimitRateOfChange = mROC;
                                cf.ControllerDatas[i].ControllerLimitDatas[j].LimitDestinationQueryResult = maxLimitDestination;
                                /*
                                string features = string.Empty;
                                foreach (var feature in cf.Features)
                                {
                                    features += feature.ToString() + " ";
                                }
                                _logger?.LogInformation("Received Limit: " + dval + " with features: " + features);
                                */
                            }
                        }
                    }
                }
            }
        }
    }
}
