module Wilnaatahl.Tests.Systems.SelectionTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Tracking
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

    /// The button is only meaningful in Move mode, so Selection declares it `MoveModeOnly` and
    /// leaves hiding it to the ViewMode system rather than reading the mode itself.
    [<Fact>]
    member _.``spawnSelectControls marks the button as Move-mode only``() =
        let buttonEntity = world.Query(With Button) |> Seq.exactlyOne
        buttonEntity |> has MoveModeOnly =! true
        (buttonEntity |> get Button).Value.disabled =! false

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

    [<Fact>]
    member _.``View mode selects the clicked node when nothing is selected``() =
        world.Add InViewMode
        let node = spawnNode world
        node |> add ClickEvent

        selectNodes world |> ignore

        (node |> has Selected) =! true

    [<Fact>]
    member _.``View mode dismisses by deselecting when the selected node is clicked``() =
        world.Add InViewMode
        let node = spawnNode world
        node |> add Selected
        node |> add ClickEvent

        selectNodes world |> ignore

        (node |> has Selected) =! false

    [<Fact>]
    member _.``View mode dismisses without selecting when another node is clicked``() =
        world.Add InViewMode
        let selected = spawnNode world
        let other = spawnNode world
        selected |> add Selected
        other |> add ClickEvent

        selectNodes world |> ignore

        // The open overlay is dismissed and the newly-clicked node is not selected.
        (selected |> has Selected) =! false
        (other |> has Selected) =! false

    [<Fact>]
    member _.``View mode stays single-select even when the select-mode button is multi-select``() =
        // Toggle the select-mode button to multi-select (only meaningful while in Move mode).
        let buttonEntity = world.Query(With Button) |> Seq.head
        buttonEntity |> add ClickEvent
        selectNodes world |> ignore
        cleanupEvents world |> ignore

        // Enter View mode and select the first node.
        world.Add InViewMode
        let node1 = spawnNode world
        node1 |> add ClickEvent
        selectNodes world |> ignore
        (node1 |> has Selected) =! true
        cleanupEvents world |> ignore

        // Clicking a second node must not leave two nodes selected despite multi-select.
        let node2 = spawnNode world
        node2 |> add ClickEvent
        selectNodes world |> ignore

        let selectedCount = world.Query(With Selected) |> Seq.length
        selectedCount =! 0
        (node1 |> has Selected) =! false
        (node2 |> has Selected) =! false

    /// A click can no longer reach a hidden control — `Events.handleClick` refuses to raise one —
    /// so `selectNodes` carries no stale-click guard. This pins that the protection really is at
    /// the source: a click raised the way the view layer raises it never arrives.
    [<Fact>]
    member _.``a click on the hidden select-mode button never reaches selectNodes``() =
        let node = spawnNode world
        node |> add Selected

        let buttonEntity = world.Query(With Button) |> Seq.exactlyOne
        let labelBefore = (buttonEntity |> get Button).Value.label
        // View mode hides the button; the view layer may still deliver one delayed click.
        buttonEntity |> add Hidden
        buttonEntity |> handleClick world

        selectNodes world |> ignore

        node |> has Selected =! true
        (buttonEntity |> get Button).Value.label =! labelBefore
