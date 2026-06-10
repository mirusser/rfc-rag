# Experimental and Specialized APIs

Do not introduce experimental APIs casually.

Use specialized or experimental features only when there is an explicit requirement, a clear benefit, and tests that cover the behavior.

Examples requiring extra care:

* C# interceptors.
* `Tensor<T>` and experimental tensor APIs.
* Post-quantum cryptography APIs.
* Preview framework APIs.
* Source-generator interception hooks.
* Chaos engineering libraries in production paths.

When using experimental APIs:

* Isolate usage behind a small abstraction.
* Document why the API is used.
* Avoid exposing the experimental API in public contracts.
* Add tests around the behavior.
* Preserve an escape path if the API changes.