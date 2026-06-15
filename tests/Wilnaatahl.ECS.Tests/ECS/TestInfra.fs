/// Provides shared test types used by both .NET and Fable test projects.
module Wilnaatahl.Tests.ECS.TestInfra

/// The exact message a query throws from UpdateEach when its value is a relation pair (the
/// fail-fast guard). Exception messages are part of the contract, so tests assert on this. Both
/// the .NET mock (TestECS) and the TypeScript wrapper (kootaWrapper.ts) must produce this exact
/// text; asserting equality here cross-checks that those two independent definitions agree.
let relationValueUpdateEachError =
    "Cannot UpdateEach a query whose value is a relation pair. Read the relation's value with ForEach instead; UpdateEach cannot read a relation pair's per-target value."

/// Runs the given action and returns the message of the exception it throws, or None if it does
/// not throw. Used to assert on fail-fast exception messages portably across .NET and Fable.
let captureExceptionMessage (action: unit -> unit) : string option =
    try
        action ()
        None
    with ex ->
        Some ex.Message

/// A mutable record type used to test UpdateEachWith change detection.
/// FreezeValue/UnfreezeValue enable the mock ECS to snapshot and compare values.
type MutableTrait = {
    mutable X: int
} with

    static member FreezeValue(m: MutableTrait) : {| X: int |} = {| X = m.X |}
    static member UnfreezeValue(i: {| X: int |}) = { X = i.X }

/// Encapsulates test world creation and disposal for both .NET and Fable.
/// Under .NET, wraps TestWorld (IDisposable). Under Fable, wraps a Koota world.
type TestWorldWrapper() =
#if FABLE_COMPILER
    let world = FableTestInfra.createTestWorld ()
#else
    do Wilnaatahl.ECS.Mocks.TestECS.install ()
    let testWorld = new Wilnaatahl.ECS.Mocks.TestWorld()
    let world = testWorld :> Wilnaatahl.ECS.IWorld
#endif

    member _.World = world

    interface System.IDisposable with
        member _.Dispose() =
#if FABLE_COMPILER
            ()
#else
            (testWorld :> System.IDisposable).Dispose()
#endif
