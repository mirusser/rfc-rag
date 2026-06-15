# RfcRag.Tests

Unit and integration tests for `RfcRag`.

## Structure

Selected files and directories:

```text
UnitTests/
  RfcParserTests.cs
  RfcRagToolsTests.cs
  RetrievalMetricsTests.cs
  AnswerEvaluationMetricsTests.cs
  AnswerEvaluationEvidenceMetricsTests.cs
  RfcRagOptionsValidatorTests.cs

IntegrationTests/
  RfcRagIntegrationTests.cs
  RetrievalQualityTests.cs
  AnswerQualityTests.cs
  EvalCommandTests.cs
  LiveApiIndexingTests.cs

Fakes/
  FakeChatClient.cs
  FakeSearchService.cs
  FakeAskService.cs

TestData/
  rfc2119.txt, rfc3986.txt, rfc8446.txt, rfc9000.txt, rfc9110.txt, rfc9999.txt
```

## Running

```bash
dotnet test tests/RfcRag.Tests/ --filter "Category!=Integration"
dotnet test tests/RfcRag.Tests/ --filter "Category=Integration"
dotnet test tests/RfcRag.Tests/ --filter "Category=RetrievalQuality"
dotnet test tests/RfcRag.Tests/ --filter "Category=AnswerQuality" --no-restore
```

`Category=AnswerQuality` is a fake-chat CI harness that validates answer verification behavior and deterministic metrics. It is not a real-model answer-quality score; use `make eval-answers` for the local real-model answer eval path.
