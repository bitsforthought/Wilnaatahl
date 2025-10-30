/// Provides shared test types used by both .NET and Fable test projects.
module Wilnaatahl.Tests.ECS.TestInfra

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
