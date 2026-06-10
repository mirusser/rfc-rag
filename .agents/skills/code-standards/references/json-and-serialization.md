# JSON and Serialization

Prefer `System.Text.Json` unless the repository already depends on another serializer for a specific reason.

Reuse `JsonSerializerOptions` instances. Do not create new options repeatedly on hot paths.

Use `JsonSerializerOptions.Web` for web-style defaults when those defaults match the contract.

Keep JSON property names, discriminator names, enum text, and wire contracts stable.

Use constants for important JSON property names and discriminators when they are referenced outside attributes or in multiple places.

Prefer source-generated JSON contexts for:

* Native AOT.
* Trimmed applications.
* Hot paths.
* Large DTO graphs.
* Library code where reflection-based serialization would be fragile.

Use explicit polymorphism configuration for polymorphic serialization. Do not deserialize arbitrary runtime types from untrusted input.

Be careful when changing records or constructors used by JSON serialization. Constructor parameters and property names can affect binding.
