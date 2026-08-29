module Wilnaatahl.Tests.Traits.ViewTraitsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``currentMode reports the mode the world was put in``() =
        world.AddWith CurrentMode Moving
        world |> currentMode =! Moving

        world.Set CurrentMode Viewing
        world |> currentMode =! Viewing

    /// A world with no mode is one whose controls were never spawned. Reporting a mode anyway
    /// would let an incomplete world run every system while silently claiming to be in one of
    /// them, so the read fails instead.
    [<Fact>]
    member _.``currentMode fails on a world that has no mode``() =
        let ex = Assert.ThrowsAny<exn>(fun () -> world |> currentMode |> ignore)

        ex.Message
        =! "The world has no CurrentMode. Spawn the view-mode controls first."

    [<Fact>]
    member _.``isViewing distinguishes the two modes``() =
        isViewing Viewing =! true
        isViewing Moving =! false
