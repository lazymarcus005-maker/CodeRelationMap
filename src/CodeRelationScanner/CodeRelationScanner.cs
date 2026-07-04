using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodeRelationScanner;

public sealed partial class CodeRelationScanner
{
    private readonly TeamRuleLoader _ruleLoader = new();

    public RelationMap Scan(string workspacePath, string? globalRulesPath = null)
    {
        var loadResult = _ruleLoader.Load(workspacePath, globalRulesPath);
        var files = Directory.Exists(workspacePath)
            ? Directory.EnumerateFiles(workspacePath, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(path => SourceFile.Read(workspacePath, path, loadResult.Rules))
                .ToList()
            : [];

        var map = new RelationMap { ActiveRules = loadResult.ActiveRules };
        map.Nodes.AddRange(files.SelectMany(file => file.Classes.Select(sourceClass => ToNode(sourceClass))));
        Validate(loadResult.Rules, files, map);
        AddAgentGuidance(loadResult.Rules, files, map);
        return map;
    }

    public string ScanToJson(string workspacePath, string? globalRulesPath = null)
    {
        return JsonSerializer.Serialize(Scan(workspacePath, globalRulesPath), ScannerJsonContext.Default.RelationMap);
    }

    private static RelationNode ToNode(SourceClass sourceClass)
    {
        return new RelationNode
        {
            NodeId = sourceClass.NodeId,
            Name = sourceClass.Name,
            Type = sourceClass.Type,
            FilePath = sourceClass.FilePath,
            Line = sourceClass.Line
        };
    }

    private static void Validate(TeamRules rules, IReadOnlyCollection<SourceFile> files, RelationMap map)
    {
        var allClasses = files.SelectMany(file => file.Classes).ToList();
        var classNames = allClasses.Select(sourceClass => sourceClass.Name).ToHashSet(StringComparer.Ordinal);
        var testClasses = allClasses.Where(sourceClass => sourceClass.Type == "test_class").ToList();
        var registrationText = string.Join('\n', files.Select(file => file.Content));

        foreach (var handler in allClasses.Where(sourceClass => sourceClass.Type == "handler"))
        {
            if (rules.Architecture.ForbidDirectHttpClientInHandler && handler.ConstructorParameters.Any(IsHttpClient))
            {
                map.RuleValidation.Violations.Add(new RuleFinding
                {
                    RuleId = "architecture.forbidDirectHttpClientInHandler",
                    Severity = "error",
                    Message = $"{handler.Name} directly depends on HttpClient. Use a typed client or service abstraction.",
                    FilePath = handler.FilePath,
                    Line = handler.ConstructorLine,
                    RelatedNodeId = handler.NodeId,
                    Confidence = "high",
                    Evidence = "constructor parameter type is HttpClient"
                });
            }

            if (rules.Architecture.RequireUnitTestForHandler && !HasRelatedTest(handler, testClasses, rules))
            {
                map.RuleValidation.Warnings.Add(new RuleFinding
                {
                    RuleId = "architecture.requireUnitTestForHandler",
                    Severity = "warning",
                    Message = $"No high-confidence unit test relation found for {handler.Name}.",
                    FilePath = handler.FilePath,
                    RelatedNodeId = handler.NodeId,
                    Confidence = "medium",
                    Evidence = $"No test class named {handler.Name}{rules.Naming.TestSuffix} or exact handler reference was found."
                });
            }
        }

        foreach (var controller in allClasses.Where(sourceClass => sourceClass.Type == "endpoint"))
        {
            if (rules.Architecture.ForbidRepositoryAccessInController && controller.ConstructorParameters.Any(parameter => IsRepository(parameter, rules)))
            {
                var repository = controller.ConstructorParameters.First(parameter => IsRepository(parameter, rules));
                map.RuleValidation.Violations.Add(new RuleFinding
                {
                    RuleId = "architecture.forbidRepositoryAccessInController",
                    Severity = "error",
                    Message = $"{controller.Name} directly depends on {repository.Type}. Use a handler or service abstraction.",
                    FilePath = controller.FilePath,
                    Line = controller.ConstructorLine,
                    RelatedNodeId = controller.NodeId,
                    Confidence = "high",
                    Evidence = $"constructor parameter type is {repository.Type}"
                });
            }

            if (rules.Architecture.RequireOpenApiMetadataForPublicEndpoint && HasHttpAction(controller) && !HasOpenApiMetadata(controller))
            {
                map.RuleValidation.Warnings.Add(new RuleFinding
                {
                    RuleId = "architecture.requireOpenApiMetadataForPublicEndpoint",
                    Severity = "warning",
                    Message = $"{controller.Name} has public HTTP actions without obvious OpenAPI metadata.",
                    FilePath = controller.FilePath,
                    RelatedNodeId = controller.NodeId,
                    Confidence = "medium",
                    Evidence = "HTTP method attribute found but no ProducesResponseType, SwaggerOperation, EndpointSummary, or OpenApi attribute was detected in the class."
                });
            }

            if (rules.Architecture.RequireValidatorForRequest)
            {
                foreach (var requestName in controller.RequestTypeNames)
                {
                    var validatorName = $"{requestName}{rules.Naming.ValidatorSuffix}";
                    if (!classNames.Contains(validatorName))
                    {
                        map.RuleValidation.Violations.Add(new RuleFinding
                        {
                            RuleId = "architecture.requireValidatorForRequest",
                            Severity = "error",
                            Message = $"{controller.Name} accepts {requestName}, but no {validatorName} was found.",
                            FilePath = controller.FilePath,
                            RelatedNodeId = controller.NodeId,
                            Confidence = "high",
                            Evidence = $"public HTTP action parameter type is {requestName}"
                        });
                    }
                }
            }
        }

        foreach (var serviceInterface in allClasses.Where(sourceClass => sourceClass.Type == "service_interface"))
        {
            var implementationName = serviceInterface.Name.TrimStart(rules.Naming.ServiceInterfacePrefix.ToCharArray());
            if (!classNames.Contains(implementationName))
            {
                map.RuleValidation.Warnings.Add(new RuleFinding
                {
                    RuleId = "architecture.requireServiceImplementation",
                    Severity = "warning",
                    Message = $"No implementation class named {implementationName} was found for {serviceInterface.Name}.",
                    FilePath = serviceInterface.FilePath,
                    Line = serviceInterface.Line,
                    RelatedNodeId = serviceInterface.NodeId,
                    Confidence = "medium",
                    Evidence = $"service interface name follows {rules.Naming.ServiceInterfacePrefix}*{rules.Naming.ServiceSuffix} convention"
                });
            }
        }

        foreach (var serviceImplementation in allClasses.Where(sourceClass => sourceClass.Type == "service_implementation"))
        {
            if (rules.Architecture.RequireDiRegistrationForService && !HasDiRegistration(serviceImplementation, registrationText))
            {
                map.RuleValidation.Warnings.Add(new RuleFinding
                {
                    RuleId = "architecture.requireDiRegistrationForService",
                    Severity = "warning",
                    Message = $"No DI registration was found for {serviceImplementation.Name}.",
                    FilePath = serviceImplementation.FilePath,
                    Line = serviceImplementation.Line,
                    RelatedNodeId = serviceImplementation.NodeId,
                    Confidence = "medium",
                    Evidence = $"No AddScoped/AddTransient/AddSingleton registration references {serviceImplementation.Name}."
                });
            }
        }
    }

    private static void AddAgentGuidance(TeamRules rules, IReadOnlyCollection<SourceFile> files, RelationMap map)
    {
        var knownFiles = files.Select(file => file.FilePath).ToList();
        foreach (var node in map.Nodes.Where(node => node.Type is "handler" or "endpoint"))
        {
            var requiredTypes = node.Type == "handler"
                ? new List<string> { "validator", "service_interface", "service_implementation", "test_class" }
                : new List<string> { "request", "handler", "validator", "response", "test_class" };

            var mustFollowRules = new List<string>();
            if (rules.Architecture.RequireValidatorForRequest)
            {
                mustFollowRules.Add("architecture.requireValidatorForRequest");
            }

            if (rules.Architecture.RequireUnitTestForHandler)
            {
                mustFollowRules.Add("architecture.requireUnitTestForHandler");
            }

            if (rules.Architecture.ForbidDirectHttpClientInHandler)
            {
                mustFollowRules.Add("architecture.forbidDirectHttpClientInHandler");
            }

            if (rules.Architecture.ForbidRepositoryAccessInController)
            {
                mustFollowRules.Add("architecture.forbidRepositoryAccessInController");
            }

            node.AgentGuidance = new AgentGuidance
            {
                RequiredRelatedNodeTypes = requiredTypes,
                MustFollowRules = mustFollowRules,
                RecommendedReferenceFiles = knownFiles
                    .Where(path => path != node.FilePath && LooksLikeReference(path, node.FilePath))
                    .Take(3)
                    .ToList()
            };
        }
    }

    private static bool LooksLikeReference(string candidate, string current)
    {
        var currentDirectory = Path.GetDirectoryName(current)?.Replace('\\', '/') ?? string.Empty;
        return !string.IsNullOrWhiteSpace(currentDirectory)
            && candidate.Replace('\\', '/').StartsWith(currentDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHttpClient(ConstructorParameter parameter)
    {
        return parameter.Type is "HttpClient" or "System.Net.Http.HttpClient";
    }

    private static bool IsRepository(ConstructorParameter parameter, TeamRules rules)
    {
        return parameter.Type.EndsWith(rules.Naming.RepositorySuffix, StringComparison.Ordinal)
            || (parameter.Type.StartsWith(rules.Naming.RepositoryInterfacePrefix, StringComparison.Ordinal)
                && parameter.Type.EndsWith(rules.Naming.RepositorySuffix, StringComparison.Ordinal));
    }

    private static bool HasRelatedTest(SourceClass handler, IReadOnlyCollection<SourceClass> testClasses, TeamRules rules)
    {
        var expectedName = $"{handler.Name}{rules.Naming.TestSuffix}";
        return testClasses.Any(testClass =>
            string.Equals(testClass.Name, expectedName, StringComparison.Ordinal)
            || testClass.Content.Contains(handler.Name, StringComparison.Ordinal));
    }

    private static bool HasDiRegistration(SourceClass serviceImplementation, string registrationText)
    {
        return DiRegistrationRegex().IsMatch(registrationText)
            && registrationText.Contains(serviceImplementation.Name, StringComparison.Ordinal);
    }

    private static bool HasHttpAction(SourceClass controller)
    {
        return HttpActionRegex().IsMatch(controller.Content);
    }

    private static bool HasOpenApiMetadata(SourceClass controller)
    {
        return OpenApiMetadataRegex().IsMatch(controller.Content);
    }

    private sealed partial class SourceFile
    {
        private SourceFile(string workspacePath, string fullPath, TeamRules rules)
        {
            FullPath = fullPath;
            FilePath = Path.GetRelativePath(workspacePath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            Content = File.ReadAllText(fullPath);
            Classes = ClassRegex()
                .Matches(Content)
                .Select(match => SourceClass.Create(this, match.Groups["kind"].Value, match.Groups["name"].Value, match.Index, rules))
                .ToList();
        }

        public string FullPath { get; }
        public string FilePath { get; }
        public string Content { get; }
        public List<SourceClass> Classes { get; }

        public static SourceFile Read(string workspacePath, string fullPath, TeamRules rules)
        {
            return new SourceFile(workspacePath, fullPath, rules);
        }

        [GeneratedRegex(@"\b(?<kind>class|interface|record)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled)]
        private static partial Regex ClassRegex();
    }

    private sealed partial class SourceClass
    {
        private SourceClass(SourceFile file, string name, string kind, int index, TeamRules rules)
        {
            Name = name;
            Kind = kind;
            FilePath = file.FilePath;
            Content = ExtractClassBody(file.Content, index);
            Line = 1 + file.Content[..index].Count(character => character == '\n');
            Type = Classify(file.FilePath, name, kind, rules);
            NodeId = $"symbol:{name}";
            ConstructorParameters = ParseConstructorParameters(name, Content, out var constructorLine);
            ConstructorLine = constructorLine > 0 ? Line + constructorLine - 1 : Line;
            RequestTypeNames = ParseRequestTypes(Content, rules);
        }

        public string Name { get; }
        public string Kind { get; }
        public string Type { get; }
        public string NodeId { get; }
        public string FilePath { get; }
        public string Content { get; }
        public int Line { get; }
        public int ConstructorLine { get; }
        public List<ConstructorParameter> ConstructorParameters { get; }
        public List<string> RequestTypeNames { get; }

        public static SourceClass Create(SourceFile file, string kind, string name, int index, TeamRules rules)
        {
            return new SourceClass(file, name, kind, index, rules);
        }

        private static string Classify(string filePath, string name, string kind, TeamRules rules)
        {
            if (IsInAnyFolder(filePath, rules.Folders.Tests) || name.EndsWith(rules.Naming.TestSuffix, StringComparison.Ordinal))
            {
                return "test_class";
            }

            if (name.EndsWith("Controller", StringComparison.Ordinal))
            {
                return "endpoint";
            }

            if (name.EndsWith(rules.Naming.HandlerSuffix, StringComparison.Ordinal))
            {
                return "handler";
            }

            if (name.EndsWith(rules.Naming.ValidatorSuffix, StringComparison.Ordinal))
            {
                return "validator";
            }

            if (kind == "interface"
                && name.StartsWith(rules.Naming.ServiceInterfacePrefix, StringComparison.Ordinal)
                && name.EndsWith(rules.Naming.ServiceSuffix, StringComparison.Ordinal))
            {
                return "service_interface";
            }

            if (name.EndsWith(rules.Naming.ServiceSuffix, StringComparison.Ordinal) || IsInAnyFolder(filePath, rules.Folders.Services))
            {
                return "service_implementation";
            }

            if (name.EndsWith(rules.Naming.RepositorySuffix, StringComparison.Ordinal) || IsInAnyFolder(filePath, rules.Folders.Repositories))
            {
                return "external_client_or_repository";
            }

            if (name.EndsWith(rules.Naming.ClientSuffix, StringComparison.Ordinal) || IsInAnyFolder(filePath, rules.Folders.Clients))
            {
                return "external_client_or_repository";
            }

            if (name.EndsWith(rules.Naming.CommandSuffix, StringComparison.Ordinal) || name.EndsWith(rules.Naming.QuerySuffix, StringComparison.Ordinal))
            {
                return "request";
            }

            if (IsInAnyFolder(filePath, rules.Folders.Controllers))
            {
                return "endpoint";
            }

            if (IsInAnyFolder(filePath, rules.Folders.Handlers))
            {
                return "handler";
            }

            if (IsInAnyFolder(filePath, rules.Folders.Validators))
            {
                return "validator";
            }

            return "unknown";
        }

        private static bool IsInAnyFolder(string filePath, IEnumerable<string> folders)
        {
            var normalized = filePath.Replace('\\', '/');
            return folders.Any(folder =>
            {
                var folderToken = folder.Replace('\\', '/').Trim('/');
                return normalized.StartsWith($"{folderToken}/", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains($"/{folderToken}/", StringComparison.OrdinalIgnoreCase);
            });
        }

        private static string ExtractClassBody(string content, int index)
        {
            var openBrace = content.IndexOf('{', index);
            if (openBrace < 0)
            {
                return content[index..];
            }

            var depth = 0;
            for (var position = openBrace; position < content.Length; position++)
            {
                if (content[position] == '{')
                {
                    depth++;
                }
                else if (content[position] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return content[index..(position + 1)];
                    }
                }
            }

            return content[index..];
        }

        private static List<ConstructorParameter> ParseConstructorParameters(string className, string classBody, out int constructorLine)
        {
            var match = Regex.Match(classBody, $@"\b{Regex.Escape(className)}\s*\((?<parameters>[^)]*)\)");
            constructorLine = match.Success ? 1 + classBody[..match.Index].Count(character => character == '\n') : 0;
            return match.Success ? ParseParameters(match.Groups["parameters"].Value) : [];
        }

        private static List<string> ParseRequestTypes(string classBody, TeamRules rules)
        {
            return MethodParameterRegex()
                .Matches(classBody)
                .SelectMany(match => ParseParameters(match.Groups["parameters"].Value))
                .Where(parameter => parameter.Type.EndsWith(rules.Naming.CommandSuffix, StringComparison.Ordinal)
                    || parameter.Type.EndsWith(rules.Naming.QuerySuffix, StringComparison.Ordinal))
                .Select(parameter => parameter.Type)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static List<ConstructorParameter> ParseParameters(string parameters)
        {
            return parameters
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(parameter => ParameterRegex().Match(parameter))
                .Where(match => match.Success)
                .Select(match => new ConstructorParameter(CleanTypeName(match.Groups["type"].Value), match.Groups["name"].Value))
                .ToList();
        }

        private static string CleanTypeName(string type)
        {
            return type
                .Replace("readonly ", string.Empty, StringComparison.Ordinal)
                .Replace("?", string.Empty, StringComparison.Ordinal)
                .Trim();
        }

        [GeneratedRegex(@"\b(?:public|internal|private|protected)\s+(?:async\s+)?[A-Za-z0-9_<>,\[\]?\.]+\s+[A-Za-z_][A-Za-z0-9_]*\s*\((?<parameters>[^)]*)\)", RegexOptions.Compiled)]
        private static partial Regex MethodParameterRegex();

        [GeneratedRegex(@"(?<type>[A-Za-z_][A-Za-z0-9_<>,\[\]?\.]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled)]
        private static partial Regex ParameterRegex();
    }

    private sealed record ConstructorParameter(string Type, string Name);

    [GeneratedRegex(@"\bAdd(?:Scoped|Transient|Singleton)\s*<", RegexOptions.Compiled)]
    private static partial Regex DiRegistrationRegex();

    [GeneratedRegex(@"\[(?:HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch|MapGet|MapPost)", RegexOptions.Compiled)]
    private static partial Regex HttpActionRegex();

    [GeneratedRegex(@"(ProducesResponseType|SwaggerOperation|EndpointSummary|OpenApi)", RegexOptions.Compiled)]
    private static partial Regex OpenApiMetadataRegex();
}
