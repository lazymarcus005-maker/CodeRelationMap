## Team Rules and Repository Overrides

ระบบต้องรองรับ Team Rules เพื่อให้ Code Relation Map ไม่ได้บอกแค่ว่า code มีความสัมพันธ์กันอย่างไร แต่บอกได้ด้วยว่า repository นี้ต้องทำงานตาม architecture และ coding convention แบบใด

Team Rules ต้องใช้ได้กับทั้ง:

```text
- Relation Scanner
- SA SubAgent
- Code Gen SubAgent
- UnitTest SubAgent
- BugResolver
- Orchestrator validation
```

### Rule Sources

ต้องรองรับ rule 2 ระดับ:

```text
1. Global Team Rules
   มาตรฐานกลางสำหรับทุก repository

2. Repository Override Rules
   ข้อยกเว้นหรือรูปแบบเฉพาะของ repository นั้น
```

ลำดับ precedence:

```text
Repository Override Rules
  > Global Team Rules
  > Scanner Default Rules
```

ให้โหลด config ตามลำดับนี้:

```text
--global-rules <path optional>
workspace/.agent/team-rules.json
workspace/.agent/repository-overrides.json
workspace/.agent/relation-scanner.rules.json
```

หาก file ไม่มี ให้ใช้ default rules และ log ว่าใช้ default rule set

---

### Required Rule Files

สร้าง sample config files ใน project example หรือ README:

```text
.agent/
  team-rules.json
  repository-overrides.json
  relation-scanner.rules.json
```

### Global Team Rules Example

```json
{
  "schemaVersion": "1.0",
  "architecture": {
    "defaultFlow": [
      "endpoint",
      "request",
      "handler",
      "service_interface",
      "service_implementation",
      "external_client_or_repository",
      "response",
      "unit_test"
    ],
    "requireHandlerForEndpoint": true,
    "requireValidatorForRequest": true,
    "requireUnitTestForHandler": true,
    "requireDiRegistrationForService": true,
    "requireOpenApiMetadataForPublicEndpoint": true,
    "externalHttpMustUseTypedClient": true,
    "forbidDirectHttpClientInHandler": true,
    "forbidRepositoryAccessInController": true
  },
  "naming": {
    "querySuffix": "Query",
    "commandSuffix": "Command",
    "handlerSuffix": "Handler",
    "validatorSuffix": "Validator",
    "serviceInterfacePrefix": "I",
    "serviceSuffix": "Service",
    "repositoryInterfacePrefix": "I",
    "repositorySuffix": "Repository",
    "clientInterfacePrefix": "I",
    "clientSuffix": "Client",
    "testSuffix": "Tests"
  },
  "folders": {
    "controllers": [
      "Controllers",
      "Features"
    ],
    "handlers": [
      "Handlers",
      "Features",
      "Application"
    ],
    "services": [
      "Services",
      "Application/Services"
    ],
    "repositories": [
      "Repositories",
      "Infrastructure/Repositories"
    ],
    "clients": [
      "Clients",
      "Infrastructure/Clients"
    ],
    "validators": [
      "Validators",
      "Features"
    ],
    "tests": [
      "Tests",
      "UnitTests"
    ]
  },
  "testing": {
    "supportedFrameworks": [
      "xUnit",
      "NUnit",
      "MSTest"
    ],
    "requireHappyPathTest": true,
    "requireValidationFailureTest": true,
    "requireDependencyFailureTest": true,
    "preferredMockLibraries": [
      "NSubstitute",
      "Moq"
    ]
  },
  "changeGuardrails": {
    "forbidUnrelatedFileChanges": true,
    "maxChangedFiles": 12,
    "requireReasonForSharedContractChange": true,
    "requireReasonForDatabaseMigration": true
  }
}
```

### Repository Override Example

```json
{
  "schemaVersion": "1.0",
  "repository": "CXPaymentAPI",
  "overrides": {
    "architecture": {
      "requireHandlerForEndpoint": false,
      "requireValidatorForRequest": false
    },
    "folders": {
      "services": [
        "Business",
        "ApplicationServices"
      ]
    },
    "testing": {
      "preferredMockLibraries": [
        "Moq"
      ]
    },
    "changeGuardrails": {
      "maxChangedFiles": 18
    }
  },
  "notes": [
    "This repository uses controller-to-service flow for legacy endpoints.",
    "New endpoints should use the current handler pattern where applicable."
  ]
}
```

---

### Scanner Requirements for Team Rules

`CodeRelationScanner` ต้อง:

1. โหลดและ merge rules ก่อน scan source code
2. เก็บ active rules summary ไว้ใน output JSON
3. ใช้ naming/folder rules เป็น classification signal
4. ใช้ architecture rules เพื่อตรวจหา missing relation เบื้องต้น
5. ห้ามสรุปว่า code ผิดแบบเด็ดขาด หาก relation มี confidence ต่ำ
6. แยกผลเป็น:

   * `violations` สำหรับ rule ที่ตรวจได้แน่นอน
   * `warnings` สำหรับ rule ที่ infer จาก naming/folder/convention
   * `informational` สำหรับข้อเสนอแนะ

ตัวอย่าง rule validation ที่ต้องรองรับ:

```text
- Endpoint มี request แต่ไม่มี validator
- Handler ไม่มี related unit test
- Service interface ไม่มี implementation
- Service implementation ไม่มี DI registration
- Handler inject HttpClient ตรง ๆ
- Controller inject repository ตรง ๆ
- New endpoint ไม่มี OpenAPI metadata หาก rule กำหนด
```

---

### Relation Map Output Extension

เพิ่ม section นี้ใน output JSON:

```json
{
  "activeRules": {
    "globalRulesPath": ".agent/team-rules.json",
    "repositoryOverridePath": ".agent/repository-overrides.json",
    "effectiveRuleVersion": "1.0"
  },
  "ruleValidation": {
    "violations": [],
    "warnings": [],
    "informational": []
  }
}
```

ตัวอย่าง violation:

```json
{
  "ruleId": "architecture.forbidDirectHttpClientInHandler",
  "severity": "error",
  "message": "GetPaymentHistoryHandler directly depends on HttpClient. Use a typed client or service abstraction.",
  "filePath": "Features/PaymentHistory/GetPaymentHistoryHandler.cs",
  "line": 18,
  "relatedNodeId": "symbol:CXPaymentAPI.Features.PaymentHistory.GetPaymentHistoryHandler",
  "confidence": "high",
  "evidence": "constructor parameter type is HttpClient"
}
```

ตัวอย่าง warning:

```json
{
  "ruleId": "architecture.requireUnitTestForHandler",
  "severity": "warning",
  "message": "No high-confidence unit test relation found for GetPaymentHistoryHandler.",
  "relatedNodeId": "symbol:CXPaymentAPI.Features.PaymentHistory.GetPaymentHistoryHandler",
  "confidence": "medium",
  "evidence": "No test class directly constructs this handler; naming convention search returned no exact match."
}
```

---

### Agent Consumption Contract

Scanner ต้องมี output ที่ agent ใช้งานได้ง่าย โดยเพิ่ม `agentGuidance` ต่อ node หรือ feature relation เมื่อเป็นไปได้

ตัวอย่าง:

```json
{
  "nodeId": "symbol:CXPaymentAPI.Features.PaymentHistory.GetPaymentHistoryHandler",
  "agentGuidance": {
    "requiredRelatedNodeTypes": [
      "validator",
      "service_interface",
      "service_implementation",
      "test_class"
    ],
    "mustFollowRules": [
      "architecture.requireValidatorForRequest",
      "architecture.requireUnitTestForHandler",
      "architecture.forbidDirectHttpClientInHandler"
    ],
    "recommendedReferenceFiles": [
      "Features/PaymentSchedule/GetPaymentScheduleHandler.cs",
      "Tests/Features/PaymentSchedule/GetPaymentScheduleHandlerTests.cs"
    ]
  }
}
```

ห้ามให้ scanner ตัดสิน business requirement แทน spec หรือ LLM
scanner มีหน้าที่บอก structure, relation, convention, validation signal และ evidence เท่านั้น

---

### Definition of Done Extension

เพิ่มเงื่อนไขเหล่านี้ใน Definition of Done:

1. Scanner โหลด Global Team Rules และ Repository Override Rules ได้
2. Rule merge precedence ถูกต้อง
3. Naming/folder classification ใช้ effective rules ได้
4. Scanner report violation/warning/informational ได้
5. ทุก violation/warning มี evidence และ confidence
6. Output JSON มี active rules และ rule validation result
7. มี unit test สำหรับ:

   * global rule loading
   * repository override precedence
   * direct HttpClient in handler violation
   * service implementation missing DI registration warning
   * handler without test warning
   * controller directly depends on repository violation
8. README อธิบายวิธีเพิ่ม rule ใหม่และวิธี override ต่อ repository
