module Wilnaatahl.Tests.Systems.SelectionTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.Selection
open Wilnaatahl.Tests.EcsTestSupport

let private spawnNode (world: IWorld) =
    world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = 0.0; y = 0.0; z = 0.0 |})

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    let sortOrder, _ = spawnSelectControls (0, world)

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnSelectControls creates button entity``() =
        sortOrder =! 1

        let buttons = world.Query(With Button) |> Seq.toList
        buttons.Length =! 1

    [<Fact>]
    member _.``clicking node in single-select mode selects it``() =
        let node = spawnNode world
        node |> add ClickEvent

        selectNodes world |> ignore

        (node |> has Selected) =! true

    [<Fact>]
    member _.``clicking selected node deselects it``() =
        let node = spawnNode world
        node |> add Selected
        node |> add ClickEvent

        selectNodes world |> ignore

        (node |> has Selected) =! false

    [<Fact>]
    member _.``single-select mode clears previous selection on new click``() =
        let node1 = spawnNode world
        let node2 = spawnNode world
        node1 |> add Selected
        node2 |> add ClickEvent

        selectNodes world |> ignore

        (node1 |> has Selected) =! false
        (node2 |> has Selected) =! true

    [<Fact>]
    member _.``background click deselects all``() =
        let node = spawnNode world
        node |> add Selected
        world.Add PointerMissedEvent

        selectNodes world |> ignore

        (node |> has Selected) =! false

    [<Fact>]
    member _.``clicking select mode button toggles multi-select``() =
        // Toggle to multi-select by clicking the button
        let buttonEntity = world.Query(With Button) |> Seq.head
        buttonEntity |> add ClickEvent
        selectNodes world |> ignore
        cleanupEvents world |> ignore

        // Now in multi-select mode: select first node
        let node1 = spawnNode world
        node1 |> add ClickEvent
        selectNodes world |> ignore
        (node1 |> has Selected) =! true
        cleanupEvents world |> ignore

        // Click second node — first should remain selected
        let node2 = spawnNode world
        node2 |> add ClickEvent
        selectNodes world |> ignore

        (node1 |> has Selected) =! true
        (node2 |> has Selected) =! true

    [<Fact>]
    member _.``clicking button clears selection and updates label``() =
        let node = spawnNode world
        node |> add Selected

        let buttonEntity = world.Query(With Button) |> Seq.head
        buttonEntity |> add ClickEvent

        selectNodes world |> ignore

        (node |> has Selected) =! false

        let buttonData = (buttonEntity |> get Button).Value
        buttonData.label =! "Single-select"
