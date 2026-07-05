# Code Relation Scanner

`CodeRelationScanner` scans a C# workspace for structural code relations and team-rule signals that agents can consume. It reports structure, convention, validation evidence, and confidence only; it does not decide business requirements.

## Rule Loading

Rules are merged with this precedence:

```text
Repository Override Rules
  > Global Team Rules
  > Scanner Default Rules
```

The loader reads these files when present:

```text
--global-rules <path optional>
workspace/.agent/team-rules.json
workspace/.agent/repository-overrides.json
workspace/.agent/relation-scanner.rules.json
```

If a file is missing, built-in defaults remain active and the scanner records an informational log in `activeRules.logs`.

## Required Rule Files

Sample files are included in this repository:

```text
.agent/
  team-rules.json
  repository-overrides.json
  relation-scanner.rules.json
```

`team-rules.json` should hold organization-wide defaults. `repository-overrides.json` should hold repository-specific exceptions inside an `overrides` object. `relation-scanner.rules.json` should hold scanner-level defaults that can be superseded by team and repository rules.

## Output Contract

The relation map includes:

```json
{
  "activeRules": {
    "globalRulesPath": ".agent/team-rules.json",
    "repositoryOverridePath": ".agent/repository-overrides.json",
    "scannerRulesPath": ".agent/relation-scanner.rules.json",
    "effectiveRuleVersion": "1.0"
  },
  "ruleValidation": {
    "violations": [],
    "warnings": [],
    "informational": []
  }
}
```

Findings include `ruleId`, `severity`, `message`, `filePath`, `line`, `relatedNodeId`, `confidence`, and `evidence`.

## Supported Validation Signals

The scanner currently reports:

- Endpoint has request but no validator
- Handler has no related unit test
- Service interface has no implementation
- Service implementation has no DI registration
- Handler directly injects `HttpClient`
- Controller directly injects repository
- Public HTTP endpoint has no obvious OpenAPI metadata

High-confidence structural violations go to `violations`. Convention-based or inferred signals go to `warnings`. Suggestions that are not rule failures should go to `informational`.

## Adding a Rule

1. Add the option to `TeamRules` or one of its child rule classes.
2. Set the built-in default in the model.
3. Add scanner validation that emits evidence and confidence.
4. Add a unit test for loading, precedence, and scanner output when applicable.
5. Document the rule in `.agent/team-rules.json` and this README.

## Overriding Per Repository

Add only the changed settings under `overrides`:

```json
{
  "schemaVersion": "1.0",
  "repository": "ExampleApi",
  "overrides": {
    "architecture": {
      "requireValidatorForRequest": false
    },
    "testing": {
      "preferredMockLibraries": [
        "Moq"
      ]
    }
  }
}
```

Partial overrides are deep-merged, so omitted settings keep the effective global or scanner default value.

## Usage

### Run from CLI

Prerequisite: install the .NET SDK that supports `net10.0`.

From the repository root:

```powershell
dotnet restore
dotnet build
```

Scan the current repository and print the relation map JSON to the console:

```powershell
dotnet run --project src/CodeRelationScanner -- .
```

Scan another workspace:

```powershell
dotnet run --project src/CodeRelationScanner -- "C:\path\to\workspace"
```

Use a global team rule file:

```powershell
dotnet run --project src/CodeRelationScanner -- . --global-rules .agent/team-rules.json
```

Write output to a file:

```powershell
dotnet run --project src/CodeRelationScanner -- . --output artifacts/relation-map.json
```

The optional `scan` verb is also supported:

```powershell
dotnet run --project src/CodeRelationScanner -- scan . --global-rules .agent/team-rules.json --output artifacts/relation-map.json
```

Show CLI help:

```powershell
dotnet run --project src/CodeRelationScanner -- --help
```

Exit codes:

- `0`: scan completed
- `1`: invalid arguments or workspace path not found
- `2`: scan failed while reading or processing files

### Use from C#

```csharp
var scanner = new CodeRelationScanner();
var map = scanner.Scan(workspacePath);
var json = scanner.ScanToJson(workspacePath, globalRulesPath);
```

Run tests:

```powershell
dotnet test
```
