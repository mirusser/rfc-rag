# Review Checklist

Before finishing a change, verify:

* The diff is focused.
* Public contracts are preserved or explicitly called out.
* Magic strings introduced or touched are justified or named.
* Cancellation tokens are passed through async I/O.
* Logging uses templates, not interpolation.
* No secrets are logged.
* Nullability is honest.
* Exceptions are specific and not swallowed.
* Tests cover changed behavior.
* New modern .NET APIs are used only where they improve clarity, performance, testability, or compatibility.
* Analyzer/build warnings are not suppressed broadly.
* Any unverified build or test step is reported.