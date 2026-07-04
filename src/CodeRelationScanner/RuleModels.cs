using System.Text.Json.Serialization;

namespace CodeRelationScanner;

public sealed record ScannerLogEntry(string Level, string Message);

public sealed class TeamRules
{
    public string SchemaVersion { get; set; } = "1.0";
    public ArchitectureRules Architecture { get; set; } = new();
    public NamingRules Naming { get; set; } = new();
    public FolderRules Folders { get; set; } = new();
    public TestingRules Testing { get; set; } = new();
    public ChangeGuardrailRules ChangeGuardrails { get; set; } = new();
}

public sealed class ArchitectureRules
{
    public List<string> DefaultFlow { get; set; } =
    [
        "endpoint",
        "request",
        "handler",
        "service_interface",
        "service_implementation",
        "external_client_or_repository",
        "response",
        "unit_test"
    ];

    public bool RequireHandlerForEndpoint { get; set; } = true;
    public bool RequireValidatorForRequest { get; set; } = true;
    public bool RequireUnitTestForHandler { get; set; } = true;
    public bool RequireDiRegistrationForService { get; set; } = true;
    public bool RequireOpenApiMetadataForPublicEndpoint { get; set; } = true;
    public bool ExternalHttpMustUseTypedClient { get; set; } = true;
    public bool ForbidDirectHttpClientInHandler { get; set; } = true;
    public bool ForbidRepositoryAccessInController { get; set; } = true;
}

public sealed class NamingRules
{
    public string QuerySuffix { get; set; } = "Query";
    public string CommandSuffix { get; set; } = "Command";
    public string HandlerSuffix { get; set; } = "Handler";
    public string ValidatorSuffix { get; set; } = "Validator";
    public string ServiceInterfacePrefix { get; set; } = "I";
    public string ServiceSuffix { get; set; } = "Service";
    public string RepositoryInterfacePrefix { get; set; } = "I";
    public string RepositorySuffix { get; set; } = "Repository";
    public string ClientInterfacePrefix { get; set; } = "I";
    public string ClientSuffix { get; set; } = "Client";
    public string TestSuffix { get; set; } = "Tests";
}

public sealed class FolderRules
{
    public List<string> Controllers { get; set; } = ["Controllers", "Features"];
    public List<string> Handlers { get; set; } = ["Handlers", "Features", "Application"];
    public List<string> Services { get; set; } = ["Services", "Application/Services"];
    public List<string> Repositories { get; set; } = ["Repositories", "Infrastructure/Repositories"];
    public List<string> Clients { get; set; } = ["Clients", "Infrastructure/Clients"];
    public List<string> Validators { get; set; } = ["Validators", "Features"];
    public List<string> Tests { get; set; } = ["Tests", "UnitTests"];
}

public sealed class TestingRules
{
    public List<string> SupportedFrameworks { get; set; } = ["xUnit", "NUnit", "MSTest"];
    public bool RequireHappyPathTest { get; set; } = true;
    public bool RequireValidationFailureTest { get; set; } = true;
    public bool RequireDependencyFailureTest { get; set; } = true;
    public List<string> PreferredMockLibraries { get; set; } = ["NSubstitute", "Moq"];
}

public sealed class ChangeGuardrailRules
{
    public bool ForbidUnrelatedFileChanges { get; set; } = true;
    public int MaxChangedFiles { get; set; } = 12;
    public bool RequireReasonForSharedContractChange { get; set; } = true;
    public bool RequireReasonForDatabaseMigration { get; set; } = true;
}

public sealed class RepositoryOverrideFile
{
    public string SchemaVersion { get; set; } = "1.0";
    public string? Repository { get; set; }
    public TeamRules? Overrides { get; set; }
    public List<string> Notes { get; set; } = [];
}

public sealed class ActiveRulesSummary
{
    public string? GlobalRulesPath { get; init; }
    public string? RepositoryOverridePath { get; init; }
    public string? ScannerRulesPath { get; init; }
    public string EffectiveRuleVersion { get; init; } = "1.0";
    public List<string> LoadedSources { get; init; } = [];
    public List<ScannerLogEntry> Logs { get; init; } = [];
}

public sealed class RuleLoadResult
{
    public required TeamRules Rules { get; init; }
    public required ActiveRulesSummary ActiveRules { get; init; }
}

public sealed class RelationMap
{
    public required ActiveRulesSummary ActiveRules { get; init; }
    public List<RelationNode> Nodes { get; init; } = [];
    public RuleValidationResult RuleValidation { get; init; } = new();
}

public sealed class RelationNode
{
    public required string NodeId { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string FilePath { get; init; }
    public int Line { get; init; }
    public AgentGuidance? AgentGuidance { get; set; }
}

public sealed class AgentGuidance
{
    public List<string> RequiredRelatedNodeTypes { get; init; } = [];
    public List<string> MustFollowRules { get; init; } = [];
    public List<string> RecommendedReferenceFiles { get; init; } = [];
}

public sealed class RuleValidationResult
{
    public List<RuleFinding> Violations { get; init; } = [];
    public List<RuleFinding> Warnings { get; init; } = [];
    public List<RuleFinding> Informational { get; init; } = [];
}

public sealed class RuleFinding
{
    public required string RuleId { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public string? RelatedNodeId { get; init; }
    public required string Confidence { get; init; }
    public required string Evidence { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(TeamRules))]
[JsonSerializable(typeof(RepositoryOverrideFile))]
[JsonSerializable(typeof(RelationMap))]
internal sealed partial class ScannerJsonContext : JsonSerializerContext;
