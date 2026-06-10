# Scope of Abstractions

Keep conventions local to the project unless sharing is intentional.

Avoid creating shared abstractions only to remove small duplication.

Prefer:

- Local constants for local contracts.
- Small helper methods for repeated behavior.
- Extension methods only when they read naturally and are likely to be reused.
- Shared packages or shared projects only when there is a real cross-project contract.

Do not introduce a “grab bag” utility class.