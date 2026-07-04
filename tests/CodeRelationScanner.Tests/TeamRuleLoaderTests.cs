using System.Text.Json;

namespace CodeRelationScanner.Tests;

public sealed class TeamRuleLoaderTests
{
    [Fact]
    public void LoadsGlobalRulesFromWorkspaceAgentDirectory()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write(".agent/team-rules.json", """
        {
          "schemaVersion": "1.0",
          "architecture": {
            "requireHandlerForEndpoint": false
          },
          "testing": {
            "preferredMockLibraries": [ "Moq" ]
          }
        }
        """);

        var result = new TeamRuleLoader().Load(workspace.Path);

        Assert.False(result.Rules.Architecture.RequireHandlerForEndpoint);
        Assert.Equal(["Moq"], result.Rules.Testing.PreferredMockLibraries);
        Assert.Equal(".agent/team-rules.json", result.ActiveRules.GlobalRulesPath);
    }

    [Fact]
    public void AppliesRepositoryOverridesAfterGlobalRules()
    {
        using var workspace = TestWorkspace.Create();
        workspace.Write(".agent/team-rules.json", """
        {
          "changeGuardrails": {
            "maxChangedFiles": 12
          },
          "testing": {
            "preferredMockLibraries": [ "NSubstitute" ]
          }
        }
        """);
        workspace.Write(".agent/repository-overrides.json", """
        {
          "schemaVersion": "1.0",
          "repository": "CXPaymentAPI",
          "overrides": {
            "changeGuardrails": {
              "maxChangedFiles": 18
            },
            "testing": {
              "preferredMockLibraries": [ "Moq" ]
            }
          }
        }
        """);

        var result = new TeamRuleLoader().Load(workspace.Path);

        Assert.Equal(18, result.Rules.ChangeGuardrails.MaxChangedFiles);
        Assert.Equal(["Moq"], result.Rules.Testing.PreferredMockLibraries);
        Assert.Equal(".agent/repository-overrides.json", result.ActiveRules.RepositoryOverridePath);
    }

    [Fact]
    public void EmitsDefaultRuleLogsWhenFilesAreMissing()
    {
        using var workspace = TestWorkspace.Create();

        var result = new TeamRuleLoader().Load(workspace.Path);

        Assert.Contains(result.ActiveRules.Logs, log => log.Message.Contains("using default rule values", StringComparison.OrdinalIgnoreCase));
    }
}
