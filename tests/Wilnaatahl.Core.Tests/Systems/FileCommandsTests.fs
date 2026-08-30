module Wilnaatahl.Tests.Systems.FileCommandsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.FileCommands
open Wilnaatahl.Tests.EcsTestSupport

let private openButton (world: IWorld) =
    world.QueryFirst(With OpenFileButton) |> Option.get

let private saveButton (world: IWorld) =
    world.QueryFirst(With SaveButton) |> Option.get

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let sortOrder, _ = spawnFileControls (0, world)
    do world.AddWith CurrentMode Moving

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnFileControls creates open and save buttons``() =
        sortOrder =! 2

        let buttons = world.Query(With Button) |> Seq.toList
        buttons.Length =! 2

        // Assert the discriminators land on the correctly-labelled buttons, so the
        // system that keys off them cannot be silently broken by a dropped .Tag().
        let openData = (world |> openButton |> get Button).Value
        openData.label =! "Open file…"
        openData.sortOrder =! 0

        let saveData = (world |> saveButton |> get Button).Value
        saveData.label =! "Save"
        saveData.sortOrder =! 1

    [<Fact>]
    member _.``clicking open button raises only the open request``() =
        (world |> openButton) |> handleClick world

        handleFileCommands world |> ignore

        world.Has OpenFileRequested =! true
        world.Has SaveRequested =! false

    [<Fact>]
    member _.``clicking save button raises only the save request``() =
        (world |> saveButton) |> handleClick world

        handleFileCommands world |> ignore

        world.Has SaveRequested =! true
        world.Has OpenFileRequested =! false

    [<Fact>]
    member _.``no click raises neither request``() =
        handleFileCommands world |> ignore

        world.Has OpenFileRequested =! false
        world.Has SaveRequested =! false

    [<Fact>]
    member _.``a raised request survives end-of-frame cleanup``() =
        // The request signals are outward requests fulfilled asynchronously by the
        // host, so unlike per-frame input events they must outlive the frame that
        // raised them: cleanupEvents must not clear them.
        (world |> openButton) |> handleClick world
        handleFileCommands world |> ignore
        cleanupEvents world |> ignore

        world.Has OpenFileRequested =! true
