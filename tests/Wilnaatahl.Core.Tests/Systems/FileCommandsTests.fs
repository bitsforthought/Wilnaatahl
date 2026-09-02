module Wilnaatahl.Tests.Systems.FileCommandsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.Intents
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.FileCommands
open Wilnaatahl.Tests.EcsTestSupport

let private openButton (world: IWorld) = world |> buttonWithLabel "Open file…"

let private saveButton (world: IWorld) = world |> buttonWithLabel "Save"

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

        let openData = (world |> openButton |> get Button).Value
        openData.label =! "Open file…"
        openData.sortOrder =! 0

        let saveData = (world |> saveButton |> get Button).Value
        saveData.label =! "Save"
        saveData.sortOrder =! 1

    [<Fact>]
    member _.``spawnFileControls declares each button's own intent``() =
        (world |> openButton |> get EmitsIntent).Value =! [ OpenFile ]
        (world |> saveButton |> get EmitsIntent).Value =! [ Save ]

    [<Fact>]
    member _.``clicking open button raises only the open request``() =
        (world |> openButton) |> handleClick world

        world |> runWithIntents handleFileCommands |> ignore

        world.Has OpenFileRequested =! true
        world.Has SaveRequested =! false

    [<Fact>]
    member _.``clicking save button raises only the save request``() =
        (world |> saveButton) |> handleClick world

        world |> runWithIntents handleFileCommands |> ignore

        world.Has SaveRequested =! true
        world.Has OpenFileRequested =! false

    [<Fact>]
    member _.``no click raises neither request``() =
        world |> runWithIntents handleFileCommands |> ignore

        world.Has OpenFileRequested =! false
        world.Has SaveRequested =! false

    [<Fact>]
    member _.``a raised request survives end-of-frame cleanup``() =
        (world |> openButton) |> handleClick world
        world |> runWithIntents handleFileCommands |> ignore
        cleanupEvents world |> ignore

        world.Has OpenFileRequested =! true

    [<Fact>]
    member _.``Clicking open and save in one frame raises both request signals``() =
        (world |> openButton) |> handleClick world
        (world |> saveButton) |> handleClick world

        world |> runWithIntents handleFileCommands |> ignore

        world.Has OpenFileRequested =! true
        world.Has SaveRequested =! true
