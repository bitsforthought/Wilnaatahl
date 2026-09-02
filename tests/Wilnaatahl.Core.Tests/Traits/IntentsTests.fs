module Wilnaatahl.Tests.Traits.IntentsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    do world.AddWith CurrentMode Viewing

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``derivedIntents is empty for a frame with no input``() = world |> derivedIntents =! []

    [<Fact>]
    member _.``derivedIntents ignores a click on an entity with no EmitsIntent``() =
        let entity = world.Spawn()
        entity |> handleClick world

        world |> derivedIntents =! []

    [<Fact>]
    member _.``derivedIntents contributes every intent a clicked entity declares, in declared order``() =
        let entity = world.Spawn(EmitsIntent.Val [ Undo; Redo ])
        entity |> handleClick world

        world |> derivedIntents =! [ Undo; Redo ]

    [<Fact>]
    member _.``derivedIntents resolves the declaration present when the snapshot is derived``() =
        let entity = world.Spawn(EmitsIntent.Val [ Undo ])
        entity |> handleClick world
        entity |> setValue EmitsIntent [ Redo ]

        world |> derivedIntents =! [ Redo ]

    [<Fact>]
    member _.``derivedIntents returns a list unaffected by later declaration changes``() =
        let entity = world.Spawn(EmitsIntent.Val [ Undo ])
        entity |> handleClick world

        let snapshot = world |> derivedIntents
        entity |> setValue EmitsIntent [ Redo ]

        snapshot =! [ Undo ]
        world |> derivedIntents =! [ Redo ]

    /// Two different entities can each declare their own intents. The entities are clicked in the
    /// opposite order to the one they were spawned in, so returning them in spawn order rather
    /// than click order would not pass.
    [<Fact>]
    member _.``derivedIntents preserves the order clicks were raised in, across entities``() =
        let first = world.Spawn(EmitsIntent.Val [ Undo ])
        let second = world.Spawn(EmitsIntent.Val [ Redo ])

        second |> handleClick world
        first |> handleClick world

        world |> derivedIntents =! [ Redo; Undo ]

    /// A click landing between two others contributes its own intents at that point in the
    /// sequence, not before or after every other click's.
    [<Fact>]
    member _.``derivedIntents interleaves several entities' intents in click order``() =
        let a = world.Spawn(EmitsIntent.Val [ Undo ])
        let b = world.Spawn(EmitsIntent.Val [ ToggleMultiSelect; ClearSelection ])
        let c = world.Spawn(EmitsIntent.Val [ Redo ])

        a |> handleClick world
        b |> handleClick world
        c |> handleClick world

        world |> derivedIntents =! [ Undo; ToggleMultiSelect; ClearSelection; Redo ]

    [<Fact>]
    member _.``derivedIntents ignores drag input``() =
        handleDragStart world
        handleDrag world 1.0 0.0 0.0
        handleDragEnd world

        world |> derivedIntents =! []
