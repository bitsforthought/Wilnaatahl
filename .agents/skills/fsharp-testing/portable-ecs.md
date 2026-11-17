# Portable ECS contracts

Keep the conformance surface minimal: test reusable ECS mock/wrapper equivalence,
not domain or app/system behaviours that belong in Core.Tests. Run the same
contract on both backends. See [ADR 0004](../../../docs/adr/0004-portable-ecs-equivalence-tests.md).

Koota is the behavioural standard even for surprising or buggy results. Replicate
its behaviour in the mock, document known bugs with issue links, and constrain
contracts to Koota's supported surface (for example object schemas, not primitive
trait values when only the mock supports them).

Use shared infrastructure for platform differences, minimizing conditional
compilation in test bodies. The main F# testing guide's Fact-only and assertion
restrictions apply. Portable `captureExceptionMessage` assertions cross-check
byte-identical messages between `TestECS.fs` and `kootaWrapper.ts`; keep expected
message constants shared, not derived from either implementation.
