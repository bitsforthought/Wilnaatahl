# Minimal portable ECS equivalence tests

Wilnaatahl keeps a minimal ECS contract suite that runs against both the .NET mock
and real Koota through Fable, while domain, view-model, and app-system tests run
against the mock in Core.Tests.
The portable suite checks that the mock and wrapper are behaviourally equivalent,
providing confidence in app tests without duplicating those tests across both
runtimes.
Because production runs as Fable-generated JavaScript in a browser, .NET-only
checks cannot replace the real-Koota contract checks, and portable tests must
respect the capabilities shared by both runtimes.

Sources: [test-project responsibilities](../../AGENTS.md#key-files--directories)
and [Fable testing constraints](../../specs/done/property-based-testing.md#constraints-and-ground-rules).
