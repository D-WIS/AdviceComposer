using DWIS.Client.ReferenceImplementation;
using DWIS.Client.ReferenceImplementation.OPCFoundation;
using System.Text.Json;
using DWIS.API.DTO;
using DWIS.AdviceComposer.Model;
using DWIS.RigOS.Common.Model;
using DWIS.RigOS.Capabilities.Controller.Model;
using Newtonsoft.Json;


namespace DWIS.AdviceComposer.Service
{
    public class Worker : BackgroundService
    {
        private ILogger<DWISClientOPCF>? _loggerDWISClient;
        private ILogger<Worker>? _logger;
        private IOPCUADWISClient? _DWISClient = null;
        private TimeSpan _loopSpan;
        private Model.AdviceComposer _adviceComposer = new Model.AdviceComposer();
 
        private Configuration Configuration { get; set; } = new Configuration();

        private QueryResult? EnablePlaceHolder { get; set; } = null;

        private Dictionary<string, (string sparql, string key)> registeredQueriesSparqls_ = new Dictionary<string, (string sparql, string key)>();

        private List<AcquiredSignals> placeHolders_ = new List<AcquiredSignals>();

        private static string _ADCSStandardInterfaceSubscriptionName = "ADCSStandardInterfaceSubscription";
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

        private bool RegisterToBlackboard(TokenizedAssignableReferenceBooleanValue? readable, IOPCUADWISClient? DWISClient, ref QueryResult? placeHolder) 
        {
            bool ok = false;
            if (DWISClient != null && readable != null && readable.Manifest != null && !string.IsNullOrEmpty(readable.SparQLQuery) && readable.SparQLVariables != null && readable.SparQLVariables.Count > 0)
            {
                ManifestFile manifestFile = readable.Manifest;
                string sparQLQuery = readable.SparQLQuery;
                List<string>? variables = readable.SparQLVariables.ToList<string>();
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
                ADCSStandardInterfaceHelper standardInterfaceHelper = new ADCSStandardInterfaceHelper();
                if (_DWISClient != null && _DWISClient.Connected && standardInterfaceHelper != null && !string.IsNullOrEmpty(standardInterfaceHelper.SparQLQuery))
                {
                    string sparql = standardInterfaceHelper.SparQLQuery;
                    var result = _DWISClient.RegisterQuery(sparql, CapabilityDescriptionCallBack);
                    if (!string.IsNullOrEmpty(result.jsonQueryDiff))
                    {
                        var queryDiff = QueryResultsDiff.FromJsonString(result.jsonQueryDiff);
                        if (queryDiff != null && queryDiff.Added != null && queryDiff.Added.Any())
                        {
                            registeredQueriesSparqls_.Add(queryDiff.QueryID, (sparql, _ADCSStandardInterfaceSubscriptionName));
                            placeHolders_.Add(AcquiredSignals.CreateWithSubscription(new string[] { standardInterfaceHelper.SparQLQuery }, new string[] { _ADCSStandardInterfaceSubscriptionName }, 0, _DWISClient));
                        }
                    }
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
            List<ActivableFunction> currentFunctions = new List<ActivableFunction>();
            Dictionary<string, QueryResult> placeHolders = new Dictionary<string, QueryResult>();
            PeriodicTimer timer = new PeriodicTimer(_loopSpan);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (placeHolders_ != null)
                {
                    var existing = placeHolders_.FirstOrDefault(ph => ph.Any() && ph.First().Key == _ADCSStandardInterfaceSubscriptionName);
                    if (existing != null && existing.TryGetValue(_ADCSStandardInterfaceSubscriptionName, out List<AcquiredSignal>? signals))
                    {
                        if (signals != null && signals.Count > 0)
                        {
                            foreach (AcquiredSignal signal in signals)
                            {
                                if (signal != null)
                                {
                                    string? json = signal.GetValue<string>();
                                    if (json != null)
                                    {
                                        try
                                        {
                                            var settings = new JsonSerializerSettings
                                            {
                                                TypeNameHandling = TypeNameHandling.Objects,
                                                Formatting = Formatting.Indented
                                            };
                                            ActivableFunction? ADCSFunctionCapability = Newtonsoft.Json.JsonConvert.DeserializeObject<ActivableFunction>(json, settings);
                                            if (ADCSFunctionCapability != null && !string.IsNullOrEmpty(ADCSFunctionCapability.Name))
                                            {
                                                bool found = false;
                                                foreach (ActivableFunction function in currentFunctions)
                                                {
                                                    if (function != null && !string.IsNullOrEmpty(function.Name) && function.Name.Equals(ADCSFunctionCapability.Name))
                                                    {
                                                        found = true;
                                                        break;
                                                    }
                                                }
                                                if (!found)
                                                {
                                                    currentFunctions.Add(ADCSFunctionCapability);
                                                    if (ADCSFunctionCapability.EnableFunction != null)
                                                    {
                                                        QueryResult? placeHolder = null;
                                                        RegisterToBlackboard(ADCSFunctionCapability.EnableFunction, _DWISClient, ref placeHolder);
                                                        if (placeHolder != null)
                                                        {
                                                            if (placeHolders.ContainsKey(ADCSFunctionCapability.Name))
                                                            {
                                                                placeHolders[ADCSFunctionCapability.Name] = placeHolder;
                                                            }
                                                            else
                                                            {
                                                                placeHolders.Add(ADCSFunctionCapability.Name, placeHolder);
                                                            }
                                                        }
                                                    }
                                                }
                                                if (ADCSFunctionCapability is ControllerFunction controllerFunction)
                                                {
                                                    if (controllerFunction.Controllers != null)
                                                    {
                                                        foreach (Controller controller in controllerFunction.Controllers)
                                                        {
                                                            if (controller != null)
                                                            {
                                                                if (controller is ControllerWithOnlyLimits controllerWithOnlyLimits)
                                                                {
                                                                    if (controllerWithOnlyLimits.ControllerLimits != null)
                                                                    {
                                                                        foreach (var kpv in controllerWithOnlyLimits.ControllerLimits)
                                                                        {
                                                                            if (kpv.Value != null)
                                                                            {

                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                                else if (controller is ControllerWithControlledVariable controllerWithControlledVariable)
                                                                {
                                                                    if (controllerWithControlledVariable.ControlledVariableReference != null)
                                                                    {

                                                                    }
                                                                    if (controller is ControllerWithLimits controllerWithLimits)
                                                                    {
                                                                        if (controllerWithLimits.ControllerLimits != null)
                                                                        {
                                                                            foreach (var kpv in controllerWithLimits.ControllerLimits)
                                                                            {
                                                                                if (kpv.Value != null)
                                                                                {

                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
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
        }
    }
}
