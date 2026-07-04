using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeRelationScanner;

public sealed class TeamRuleLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public RuleLoadResult Load(string workspacePath, string? globalRulesPath = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            throw new ArgumentException("Workspace path is required.", nameof(workspacePath));
        }

        var logs = new List<ScannerLogEntry>();
        var loadedSources = new List<string>();
        var agentDirectory = Path.Combine(workspacePath, ".agent");
        var scannerRulesPath = Path.Combine(agentDirectory, "relation-scanner.rules.json");
        var workspaceGlobalRulesPath = Path.Combine(agentDirectory, "team-rules.json");
        var effectiveGlobalRulesPath = globalRulesPath ?? workspaceGlobalRulesPath;
        var repositoryOverridePath = Path.Combine(agentDirectory, "repository-overrides.json");

        JsonNode effectiveRules = JsonSerializer.SerializeToNode(new TeamRules(), JsonOptions)!;

        MergeIfExists(scannerRulesPath, "Scanner default rules", effectiveRules, loadedSources, logs);
        MergeIfExists(effectiveGlobalRulesPath, "Global team rules", effectiveRules, loadedSources, logs);
        MergeIfExists(repositoryOverridePath, "Repository override rules", effectiveRules, loadedSources, logs, unwrapOverrides: true);

        var rules = effectiveRules.Deserialize<TeamRules>(JsonOptions) ?? new TeamRules();

        return new RuleLoadResult
        {
            Rules = rules,
            ActiveRules = new ActiveRulesSummary
            {
                GlobalRulesPath = File.Exists(effectiveGlobalRulesPath) ? NormalizePath(effectiveGlobalRulesPath, workspacePath) : null,
                RepositoryOverridePath = File.Exists(repositoryOverridePath) ? NormalizePath(repositoryOverridePath, workspacePath) : null,
                ScannerRulesPath = File.Exists(scannerRulesPath) ? NormalizePath(scannerRulesPath, workspacePath) : null,
                EffectiveRuleVersion = rules.SchemaVersion,
                LoadedSources = loadedSources,
                Logs = logs
            }
        };
    }

    private static void MergeIfExists(
        string path,
        string label,
        JsonNode target,
        List<string> loadedSources,
        List<ScannerLogEntry> logs,
        bool unwrapOverrides = false)
    {
        if (!File.Exists(path))
        {
            logs.Add(new ScannerLogEntry("Information", $"{label} not found at {path}; using default rule values for missing settings."));
            return;
        }

        var source = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        if (source is null)
        {
            logs.Add(new ScannerLogEntry("Warning", $"{label} at {path} was empty and was ignored."));
            return;
        }

        if (unwrapOverrides)
        {
            source = source["overrides"] ?? source;
        }

        DeepMerge(target, source);
        loadedSources.Add(path);
        logs.Add(new ScannerLogEntry("Information", $"Loaded {label} from {path}."));
    }

    private static void DeepMerge(JsonNode target, JsonNode source)
    {
        if (target is not JsonObject targetObject || source is not JsonObject sourceObject)
        {
            return;
        }

        foreach (var (key, sourceValue) in sourceObject)
        {
            if (sourceValue is null)
            {
                continue;
            }

            if (targetObject[key] is JsonObject existingChild && sourceValue is JsonObject sourceChild)
            {
                DeepMerge(existingChild, sourceChild);
                continue;
            }

            targetObject[key] = sourceValue.DeepClone();
        }
    }

    private static string NormalizePath(string path, string workspacePath)
    {
        var relativePath = Path.GetRelativePath(workspacePath, path);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
