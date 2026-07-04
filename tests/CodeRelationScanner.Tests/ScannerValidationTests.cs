namespace CodeRelationScanner.Tests;

public sealed class ScannerValidationTests
{
    [Fact]
    public void ReportsDirectHttpClientInHandlerViolation()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Features/PaymentHistory/GetPaymentHistoryHandler.cs", """
        using System.Net.Http;

        public sealed class GetPaymentHistoryHandler
        {
            public GetPaymentHistoryHandler(HttpClient httpClient)
            {
            }
        }
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        var finding = Assert.Single(map.RuleValidation.Violations, finding => finding.RuleId == "architecture.forbidDirectHttpClientInHandler");
        Assert.Equal("high", finding.Confidence);
        Assert.Contains("HttpClient", finding.Evidence);
    }

    [Fact]
    public void ReportsServiceImplementationMissingDiRegistrationWarning()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Services/PaymentService.cs", """
        public interface IPaymentService
        {
        }

        public sealed class PaymentService : IPaymentService
        {
        }
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        var finding = Assert.Single(map.RuleValidation.Warnings, finding => finding.RuleId == "architecture.requireDiRegistrationForService");
        Assert.Equal("medium", finding.Confidence);
        Assert.Contains("PaymentService", finding.Evidence);
    }

    [Fact]
    public void DoesNotReportDiRegistrationWarningWhenServiceIsRegistered()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Services/PaymentService.cs", """
        public interface IPaymentService
        {
        }

        public sealed class PaymentService : IPaymentService
        {
        }
        """);
        workspace.Write("Program.cs", """
        services.AddScoped<IPaymentService, PaymentService>();
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        Assert.DoesNotContain(map.RuleValidation.Warnings, finding => finding.RuleId == "architecture.requireDiRegistrationForService");
    }

    [Fact]
    public void ReportsHandlerWithoutTestWarning()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Features/PaymentHistory/GetPaymentHistoryHandler.cs", """
        public sealed class GetPaymentHistoryHandler
        {
        }
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        var finding = Assert.Single(map.RuleValidation.Warnings, finding => finding.RuleId == "architecture.requireUnitTestForHandler");
        Assert.Contains("GetPaymentHistoryHandler", finding.Message);
    }

    [Fact]
    public void DoesNotReportHandlerTestWarningWhenRelatedTestExists()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Features/PaymentHistory/GetPaymentHistoryHandler.cs", """
        public sealed class GetPaymentHistoryHandler
        {
        }
        """);
        workspace.Write("Tests/Features/PaymentHistory/GetPaymentHistoryHandlerTests.cs", """
        public sealed class GetPaymentHistoryHandlerTests
        {
        }
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        Assert.DoesNotContain(map.RuleValidation.Warnings, finding => finding.RuleId == "architecture.requireUnitTestForHandler");
    }

    [Fact]
    public void ReportsControllerDirectlyDependsOnRepositoryViolation()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Controllers/PaymentController.cs", """
        public sealed class PaymentController
        {
            public PaymentController(IPaymentRepository repository)
            {
            }
        }
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        var finding = Assert.Single(map.RuleValidation.Violations, finding => finding.RuleId == "architecture.forbidRepositoryAccessInController");
        Assert.Equal("high", finding.Confidence);
        Assert.Contains("IPaymentRepository", finding.Evidence);
    }

    [Fact]
    public void ReportsMissingValidatorForEndpointRequest()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write("Controllers/PaymentController.cs", """
        public sealed class PaymentController
        {
            [HttpPost]
            public IActionResult Create(CreatePaymentCommand command) => null!;
        }

        public sealed record CreatePaymentCommand;
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        var finding = Assert.Single(map.RuleValidation.Violations, finding => finding.RuleId == "architecture.requireValidatorForRequest");
        Assert.Contains("CreatePaymentCommandValidator", finding.Message);
    }

    [Fact]
    public void IncludesActiveRulesAndAgentGuidanceInMap()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write(".agent/team-rules.json", """
        {
          "schemaVersion": "1.0"
        }
        """);
        workspace.Write("Features/PaymentHistory/GetPaymentHistoryHandler.cs", """
        public sealed class GetPaymentHistoryHandler
        {
        }
        """);

        var map = new CodeRelationScanner().Scan(workspace.Path);

        Assert.Equal(".agent/team-rules.json", map.ActiveRules.GlobalRulesPath);
        var node = Assert.Single(map.Nodes, node => node.Name == "GetPaymentHistoryHandler");
        Assert.NotNull(node.AgentGuidance);
        Assert.Contains("architecture.requireUnitTestForHandler", node.AgentGuidance!.MustFollowRules);
    }
}
