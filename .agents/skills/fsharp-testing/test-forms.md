# Test forms and generators

Use a theory for a fixed table of cases, regardless of argument types.
If the contract is universal ("only this field changes"), state it as a
property instead. A test echoing implementation steps often hides an invariant:
look for idempotence, inverse/round-trip laws, or metamorphic relationships.

Use FsCheck properties for those universal laws, and theories for enumerated
input/output classes. Keep concrete directional or wire-format examples alongside
an inverse property: coordinated mistakes in both directions can pass round-trip.
See [Choosing properties for property-based testing](https://fsharpforfunandprofit.com/posts/property-based-testing-2/).

Generators must reach the behaviour under test. Draw collision-sensitive strings
or names from a small shared pool; unconstrained strings almost never collide.
Scope properties to the cases where they hold and explain the restriction in the
doc comment. For floats, use bounded, NaN/infinity-free generators and epsilon
comparisons. Leave seeds random; failures provide reproducible replay seeds.

FsCheck is .NET-only, via `FsCheck.Xunit.v3` matching the project's xUnit major.
Its F# API (`Gen`, `Arb`, `Prop`, `gen { }`, `==>`) requires `open FsCheck.FSharp`.
For internal types, use `Prop.forAll` rather than exposing the type in a public
property parameter (FS0410). Never add FsCheck to the portable project.

`MemberData` sources are non-private module `let` bindings so xUnit can discover
public static members. Use typed `TheoryData<...>` and `struct (...)` tuple rows,
not `obj[]` or per-cell boxing. See `TransformTests.fs` for
`TheoryData<string, Pdeek>` and date-field examples.
