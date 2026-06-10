# Dependency Injection

Use constructor injection for dependencies.

Keep services small, focused, and easy to test.

Avoid service locator patterns and direct calls to `IServiceProvider` except in composition roots, factories, or framework integration code.

Avoid static mutable state.

Use options records/classes for configuration.

Use keyed services when there are multiple implementations of the same abstraction and the key is part of the composition contract.

```csharp
services.AddKeyedSingleton<ICommandHandler, StartCommandHandler>(CommandNames.Start);
services.AddKeyedSingleton<ICommandHandler, StopCommandHandler>(CommandNames.Stop);
```

Keep keyed-service keys as constants or strongly typed conventions. Do not repeat raw key strings across registration and resolution.

Prefer named or typed `HttpClient` registrations over manually constructing HttpClient.