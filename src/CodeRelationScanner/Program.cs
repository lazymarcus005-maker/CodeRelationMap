namespace CodeRelationScanner;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || HasHelpFlag(args))
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        var index = 0;
        if (string.Equals(args[index], "scan", StringComparison.OrdinalIgnoreCase))
        {
            index++;
        }

        if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Missing workspace path.");
            PrintUsage();
            return 1;
        }

        var workspacePath = Path.GetFullPath(args[index++]);
        string? globalRulesPath = null;
        string? outputPath = null;

        while (index < args.Length)
        {
            var option = args[index++];
            switch (option)
            {
                case "--global-rules":
                    if (!TryReadOptionValue(args, ref index, option, out globalRulesPath))
                    {
                        return 1;
                    }

                    globalRulesPath = Path.GetFullPath(globalRulesPath);
                    break;

                case "--output":
                case "-o":
                    if (!TryReadOptionValue(args, ref index, option, out outputPath))
                    {
                        return 1;
                    }

                    outputPath = Path.GetFullPath(outputPath);
                    break;

                default:
                    Console.Error.WriteLine($"Unknown option: {option}");
                    PrintUsage();
                    return 1;
            }
        }

        if (!Directory.Exists(workspacePath))
        {
            Console.Error.WriteLine($"Workspace path does not exist: {workspacePath}");
            return 1;
        }

        try
        {
            var scanner = new CodeRelationScanner();
            var json = scanner.ScanToJson(workspacePath, globalRulesPath);

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                Console.WriteLine(json);
            }
            else
            {
                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                File.WriteAllText(outputPath, json);
                Console.Error.WriteLine($"Relation map written to {outputPath}");
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Scan failed: {exception.Message}");
            return 2;
        }
    }

    private static bool TryReadOptionValue(string[] args, ref int index, string option, out string value)
    {
        if (index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Missing value for {option}.");
            PrintUsage();
            value = string.Empty;
            return false;
        }

        value = args[index++];
        return true;
    }

    private static bool HasHelpFlag(IEnumerable<string> args)
    {
        return args.Any(arg => arg is "--help" or "-h");
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
        Usage:
          CodeRelationScanner <workspace> [--global-rules <path>] [--output <path>]
          CodeRelationScanner scan <workspace> [--global-rules <path>] [--output <path>]

        Examples:
          dotnet run --project src/CodeRelationScanner -- .
          dotnet run --project src/CodeRelationScanner -- . --global-rules .agent/team-rules.json
          dotnet run --project src/CodeRelationScanner -- . --output artifacts/relation-map.json
        """);
    }
}
