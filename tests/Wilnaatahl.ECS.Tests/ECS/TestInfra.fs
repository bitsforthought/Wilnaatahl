/// Provides shared test types used by both .NET and Fable test projects.
module Wilnaatahl.Tests.ECS.TestInfra

/// The prefix of the exception message thrown when a value is set for a relation that is not
/// present on the subject entity. Both backends (the .NET mock and the Koota wrapper) throw a
/// message that STARTS WITH this text; the trailing entity id is non-deterministic, so tests assert
/// StartsWith rather than equality. The literal is duplicated verbatim in TestECS.fs (the mock) and
/// kootaWrapper.ts (the wrapper) because those live in the source project and cannot reference this
/// test module; this binding is the single canonical value the tests assert against.
let relationNotPresentError =
    "Cannot set a value for a relation that is not present on the subject entity"

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
