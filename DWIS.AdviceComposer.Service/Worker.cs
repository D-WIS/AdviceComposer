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

        private Dictionary<string, Entry> RegisteredQueries { get; set; } = new Dictionary<string, Entry>();

        private object lock_ = new object();

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
        private void ManageControllerFunction(ControllerFunction controllerFunction, ActivableFunction activableFunction)
        {
            bool found = false;
            foreach (var kvp1 in ControlFunctionDictionary)
            {
                if (kvp1.Value.src != null && kvp1.Value.src.ControllerFunction != null && !string.IsNullOrEmpty(kvp1.Value.src.ControllerFunction.Name) && kvp1.Value.src.ControllerFunction.Name.Equals(activableFunction.Name))
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                Guid controlFunctionID = Guid.NewGuid();
                ControlFunctionData controlFunctionData = new ControlFunctionData();
                controlFunctionData.ControllerFunction = controllerFunction;
                ControlFunctionDictionary.Add(controlFunctionID, new (controlFunctionData, null, DateTime.MinValue));

                if (controllerFunction.ContextSemanticInfo != null)
                {
                    controlFunctionData.ContextID = Guid.NewGuid();
                    RegisterQuery(controllerFunction.ContextSemanticInfo, RegisteredQueries, controlFunctionData.ContextID);
                }
                if (controllerFunction.Parameters != null)
                {
                    controlFunctionData.ParametersID = Guid.NewGuid();
                    RegisterQueryAlternate(controllerFunction.Parameters, RegisteredQueries, controlFunctionData.ParametersID);
                }
                if (controllerFunction.Parameters != null &&
                    !string.IsNullOrEmpty(controllerFunction.Parameters.SparQLQuery) &&
                    controllerFunction.Parameters.SparQLVariables != null &&
                    controllerFunction.Parameters.SparQLVariables.Count > 0)
                {
                    QueryResult? result = null;
                    RegisterToBlackboard(controllerFunction.Parameters, _DWISClient, ref result);
                    if (result != null)
                    {
                        controlFunctionData.ParametersDestinationID = Guid.NewGuid();
                        PlaceHolders.Add(controlFunctionData.ParametersDestinationID, result);
                    }
                }
                if (controllerFunction.Controllers != null)
                {
                    foreach (Controller controller in controllerFunction.Controllers)
                    {
                        if (controller != null)
                        {
                            ControlData controllerData = new ControlData();
                            controlFunctionData.controllerDatas.Add(controllerData);
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
                            if (controller is ControllerWithOnlyLimits controllerWithOnlyLimits)
                            {
                                if (controllerWithOnlyLimits.ControllerLimits != null)
                                {
                                    ManageLimits(controllerWithOnlyLimits.ControllerLimits, controllerData);
                                }
                            }
                            else if (controller is ControllerWithControlledVariable controllerWithControlledVariable)
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
                                if (controller is ControllerWithLimits controllerWithLimits)
                                {
                                    ManageLimits(controllerWithLimits.ControllerLimits, controllerData);
                                }
                            }
                        }
                    }
                }
            }
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
                            entryContext.LiveValues.Count > 0)
                        {
                            Dictionary<string, ControllerFunctionData> availableDatas = new Dictionary<string, ControllerFunctionData>();
                            foreach (var kvp2 in entryContext.LiveValues)
                            {
                                if (kvp2.Value != null && kvp2.Value.val != null && kvp2.Value.val is string json)
                                {
                                    var settings = new JsonSerializerSettings
                                    {
                                        TypeNameHandling = TypeNameHandling.Objects,
                                        Formatting = Formatting.Indented
                                    };
                                    try
                                    {
                                        DWISContext? context = JsonConvert.DeserializeObject<DWISContext>(json, settings);
                                        if (context != null)
                                        {
                                            List<Vocabulary.Schemas.Nouns.Enum> features = new List<Vocabulary.Schemas.Nouns.Enum>();
                                            if (context.CapabilityPreferences != null)
                                            {
                                                foreach (var feature in context.CapabilityPreferences)
                                                {
                                                    features.Add(feature);
                                                }
                                            }
                                            ManageParameters(kvp.Value.src, availableDatas);
                                            FindAvailableDatas(availableDatas, kvp.Value.src.controllerDatas, kvp.Value.src);
                                            List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)> withOnlyLimits = new List<(List<Vocabulary.Schemas.Nouns.Enum> features, int, ControllerFunctionData data)>();
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
                                            if (chosenControllerFunction != null)
                                            {
                                                if (chosenControllerFunction.Parameters != null && chosenControllerFunction.ParametersDestinationQueryResult != null)
                                                {
                                                    SendValue(chosenControllerFunction.ParametersDestinationQueryResult, chosenControllerFunction.Parameters);
                                                }
                                                foreach (var wol in withOnlyLimits)
                                                {
                                                    if (wol.data != null)
                                                    {
                                                        MergeLimits(chosenControllerFunction, wol.data);
                                                    }
                                                }
                                                ProcessRateOfChange(chosenControllerFunction, kvp.Key);
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
                                                                if (l != null)
                                                                {
                                                                    if (l.LimitDestinationQueryResult != null && l.LimitRecommendation != null)
                                                                    {
                                                                        SendValue(l.LimitDestinationQueryResult, l.LimitRecommendation);
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            break;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger?.LogError(ex.ToString());
                                    }
                                }
                            }
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
                        if (intersect != null && intersect.Count > 0)
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
