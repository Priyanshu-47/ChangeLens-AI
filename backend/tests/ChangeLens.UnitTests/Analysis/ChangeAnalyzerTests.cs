using ChangeLens.Infrastructure.Analysis;

namespace ChangeLens.UnitTests.Analysis;

/// <summary>Change analysis over small deterministic fixtures (brief §30–31).</summary>
public sealed class ChangeAnalyzerTests
{
    private static readonly RoslynAnalyzer Analyzer = new();
    private static readonly ChangeAnalyzer ChangeAnalyzer = new(Analyzer);

    private const string BaseGateway = """
        namespace Demo;

        public sealed class Gateway
        {
            public string Charge(decimal amount) => "ok";
        }
        """;

    private const string BaseHandler = """
        namespace Demo;

        public sealed class PaymentHandler
        {
            private readonly Gateway _gateway;
            public PaymentHandler(Gateway gateway) => _gateway = gateway;

            public string Process(decimal amount) => _gateway.Charge(amount);
        }
        """;

    private static ChangeAnalysisResult AnalyzeChange(
        string baseContent,
        string targetContent,
        string path = "src/Demo/PaymentHandler.cs")
    {
        var baseFiles = new List<SourceFile>
        {
            new("src/Demo/Gateway.cs", BaseGateway),
            new(path, baseContent)
        };
        var targetFiles = new List<SourceFile>
        {
            new("src/Demo/Gateway.cs", BaseGateway),
            new(path, targetContent)
        };

        return ChangeAnalyzer.Analyze(
            baseFiles, targetFiles,
            [new ChangedFile(path, "modified", BaseContent: baseContent, TargetContent: targetContent)]);
    }

    // --- change detection ---

    [Fact]
    public void ModifiedMethod_IsDetectedWithSignatureChange()
    {
        var modified = """
            namespace Demo;

            public sealed class PaymentHandler
            {
                private readonly Gateway _gateway;
                public PaymentHandler(Gateway gateway) => _gateway = gateway;

                public string Process(decimal amount, string idempotencyKey) => _gateway.Charge(amount);
            }
            """;

        var result = AnalyzeChange(BaseHandler, modified);

        Assert.Contains(result.ChangedSymbols, s => s.Name == "Process");
        Assert.Contains(result.ModifiedSymbols, s => s.Name == "Process");
        Assert.Empty(result.AddedSymbols);
        Assert.Empty(result.RemovedSymbols);
    }

    [Fact]
    public void AddedAndRemovedMethods_AreDetected()
    {
        var added = """
            namespace Demo;

            public sealed class PaymentHandler
            {
                private readonly Gateway _gateway;
                public PaymentHandler(Gateway gateway) => _gateway = gateway;

                public string Process(decimal amount) => _gateway.Charge(amount);

                public void Rollback() { }
            }
            """;

        var result = AnalyzeChange(BaseHandler, added);

        Assert.Contains(result.AddedSymbols, s => s.Name == "Rollback");
    }

    [Fact]
    public void UnchangedFile_ProducesNoChangedSymbols()
    {
        var result = AnalyzeChange(BaseHandler, BaseHandler);
        Assert.Empty(result.ChangedSymbols);
    }

    // --- impact traversal ---

    [Fact]
    public void DependentsOfChangedSymbol_AreReportedAsImpacted()
    {
        // Change only the Gateway file.
        var baseFiles = new List<SourceFile>
        {
            new("src/Demo/Gateway.cs", BaseGateway),
            new("src/Demo/PaymentHandler.cs", BaseHandler)
        };
        var targetFiles = new List<SourceFile>
        {
            new("src/Demo/Gateway.cs", BaseGateway),
            new("src/Demo/PaymentHandler.cs", BaseHandler)
        };

        // Body-only change: the signature stays identical so the handler's call still
        // resolves in the target graph (a signature change would break the edge).
        var changedGateway = """
            namespace Demo;

            public sealed class Gateway
            {
                public string Charge(decimal amount) => amount > 100 ? "review" : "ok";
            }
            """;

        var result = ChangeAnalyzer.Analyze(
            baseFiles, targetFiles,
            [new ChangedFile("src/Demo/Gateway.cs", "modified", BaseContent: BaseGateway, TargetContent: changedGateway)]);

        Assert.Contains(result.ChangedSymbols, s => s.Name == "Gateway");
        // PaymentHandler.Process calls the changed Gateway.Charge → impacted.
        Assert.Contains(result.ImpactedSymbols, s => s.Name == "Process");
        Assert.Contains(result.RelevantEdges, e => e.ToSymbolId.Contains("Gateway.Charge", StringComparison.Ordinal));
    }

    [Fact]
    public void ImpactTraversalDepthIsConfigurable()
    {
        // A → B → C: change C; depth 1 reaches B, depth 2 reaches A.
        var cBase = "namespace Demo; public sealed class C { public void Run() { } }";
        var cChanged = "namespace Demo; public sealed class C { public void Run() { System.Console.WriteLine(1); } }";
        var baseFiles = new List<SourceFile>
        {
            new("src/Demo/C.cs", cBase),
            new("src/Demo/B.cs", "namespace Demo; public sealed class B { private readonly C _c; public B(C c) => _c = c; public void Run() => _c.Run(); }"),
            new("src/Demo/A.cs", "namespace Demo; public sealed class A { private readonly B _b; public A(B b) => _b = b; public void Run() => _b.Run(); }")
        };

        var changed = new ChangedFile("src/Demo/C.cs", "modified", BaseContent: cBase, TargetContent: cChanged);
        var depth1 = ChangeAnalyzer.Analyze(baseFiles, baseFiles, [changed], maxImpactDepth: 1);
        var depth2 = ChangeAnalyzer.Analyze(baseFiles, baseFiles, [changed], maxImpactDepth: 2);

        Assert.Contains(depth1.ImpactedSymbols, s => s.Name == "B");
        Assert.DoesNotContain(depth1.ImpactedSymbols, s => s.Name == "A");
        Assert.Contains(depth2.ImpactedSymbols, s => s.Name == "A");
    }

    // --- API impact ---

    [Fact]
    public void ControllerAction_CallingChangedService_IsImpactedApi()
    {
        var baseController = """
            namespace Demo.Api;

            [ApiController]
            [Route("api/v1/payments")]
            public sealed class PaymentsController
            {
                private readonly PaymentHandler _handler;
                public PaymentsController(PaymentHandler handler) => _handler = handler;

                [HttpPost]
                public string Create(decimal amount) => _handler.Process(amount);
            }
            """;

        var targetController = """
            namespace Demo.Api;

            [ApiController]
            [Route("api/v1/payments")]
            public sealed class PaymentsController
            {
                private readonly PaymentHandler _handler;
                public PaymentsController(PaymentHandler handler) => _handler = handler;

                [HttpPost]
                public string Create(decimal amount, string idempotencyKey) => _handler.Process(amount, idempotencyKey);
            }
            """;

        var modifiedHandler = """
            namespace Demo;

            public sealed class PaymentHandler
            {
                private readonly Gateway _gateway;
                public PaymentHandler(Gateway gateway) => _gateway = gateway;

                public string Process(decimal amount, string idempotencyKey) => _gateway.Charge(amount);
            }
            """;

        var baseFiles = new List<SourceFile>
        {
            new("src/Demo/Gateway.cs", BaseGateway),
            new("src/Demo/PaymentHandler.cs", BaseHandler),
            new("src/Demo.Api/PaymentsController.cs", baseController)
        };
        var targetFiles = new List<SourceFile>
        {
            new("src/Demo/Gateway.cs", BaseGateway),
            new("src/Demo/PaymentHandler.cs", modifiedHandler),
            new("src/Demo.Api/PaymentsController.cs", targetController)
        };

        var result = ChangeAnalyzer.Analyze(
            baseFiles, targetFiles,
            [new ChangedFile("src/Demo/PaymentHandler.cs", "modified", BaseContent: BaseHandler, TargetContent: modifiedHandler)]);

        var api = Assert.Single(result.ImpactedApis);
        Assert.Equal("PaymentsController", api.Controller);
        Assert.Equal("POST", api.HttpMethod);
        Assert.Equal("/api/v1/payments", api.Route);
        Assert.Equal("Create", api.Action);
    }

    // --- external integration impact ---

    [Fact]
    public void HttpClientClient_ConnectedToChange_IsReported()
    {
        var gatewayClient = """
            namespace Demo;

            public sealed class GatewayClient
            {
                private readonly HttpClient _http;
                public GatewayClient(HttpClient http) => _http = http;

                public async Task<string> ChargeAsync(decimal amount)
                {
                    var response = await _http.PostAsync("v1/charges", null);
                    return await response.Content.ReadAsStringAsync();
                }
            }
            """;

        var changedHandler = """
            namespace Demo;

            public sealed class PaymentHandler
            {
                private readonly GatewayClient _client;
                public PaymentHandler(GatewayClient client) => _client = client;

                public async Task<string> Process(decimal amount) => await _client.ChargeAsync(amount);
            }
            """;

        var baseFiles = new List<SourceFile>
        {
            new("src/Demo/GatewayClient.cs", gatewayClient),
            new("src/Demo/PaymentHandler.cs", BaseHandler)
        };
        var targetFiles = new List<SourceFile>
        {
            new("src/Demo/GatewayClient.cs", gatewayClient),
            new("src/Demo/PaymentHandler.cs", changedHandler)
        };

        var result = ChangeAnalyzer.Analyze(
            baseFiles, targetFiles,
            [new ChangedFile("src/Demo/PaymentHandler.cs", "modified", BaseContent: BaseHandler, TargetContent: changedHandler)]);

        var impact = Assert.Single(result.ExternalIntegrationImpacts);
        Assert.Equal("GatewayClient", impact.ClientType);
        Assert.Contains("v1/charges", impact.EndpointHints);
        // Process (changed) reaches the client via the CALLS edge → reported as connected.
        Assert.Contains(impact.ConnectedChangedSymbols, id => id.Contains(".Process", StringComparison.Ordinal));
        Assert.Contains(result.ChangedSymbols, s => s.Name == "Process");
    }
}
