module Wilnaatahl.Tests.EcsTestSupport

open Wilnaatahl.ECS
open Wilnaatahl.ECS.Mocks

/// A disposable wrapper around TestWorld that installs the TestECS mock
/// and provides access to the IWorld interface.
type EcsWorld() =
    do TestECS.install ()
    let testWorld = new TestWorld()
    let world = testWorld :> IWorld

    member _.World = world

    interface System.IDisposable with
        member _.Dispose() =
            (testWorld :> System.IDisposable).Dispose()
