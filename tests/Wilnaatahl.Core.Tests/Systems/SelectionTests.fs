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

    // Selection reads the mode when it interprets a node click, and these tests spawn the select
    // controls without the mode button, so establish the mode the button would have set. Move is
    // the mode this button is available in; the View-mode tests below switch.
    do world |> enterMode Moving

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
        let buttonEntity = world |> buttonWithLabel "Multi-select"
        buttonEntity |> has MoveModeOnly =! true
        isButtonDisabled buttonEntity =! false

    [<Fact>]
    member _.``Clicking node in single-select mode selects it``() =
        let node = spawnNode world
        node |> handleClick world

        selectNodes world |> ignore

        (node |> has Selected) =! true

    [<Fact>]
    member _.``Clicking selected node deselects it``() =
        let node = spawnNode world
        node |> add Selected
        node |> handleClick world

        selectNodes world |> ignore

        (node |> has Selected) =! false

    [<Fact>]
    member _.``Single-select mode clears previous selection on new click``() =
        let node1 = spawnNode world
        let node2 = spawnNode world
        node1 |> add Selected
        node2 |> handleClick world

        selectNodes world |> ignore

        (node1 |> has Selected) =! false
        (node2 |> has Selected) =! true

    /// Two nodes can be tapped in one frame, and only one selection can remain in the default
    /// single-select mode. Each click is applied in the order it was raised, so the second click
    /// replaces the first.
    [<Fact>]
    member _.``Clicking two nodes in one frame leaves the last one clicked selected``() =
        let spawnedFirst = spawnNode world
        let spawnedSecond = spawnNode world

        spawnedSecond |> handleClick world
        spawnedFirst |> handleClick world

        selectNodes world |> ignore

        (spawnedSecond |> has Selected) =! false
        (spawnedFirst |> has Selected) =! true

    [<Fact>]
    member _.``Background click deselects all``() =
        let node = spawnNode world
        node |> add Selected
        world |> handlePointerMissed

        selectNodes world |> ignore

        (node |> has Selected) =! false

    [<Fact>]
    member _.``Background miss followed by node click leaves the node selected``() =
        let node = spawnNode world
        node |> add Selected

        world |> handlePointerMissed
        node |> handleClick world
        selectNodes world |> ignore

        node |> has Selected =! true

    [<Fact>]
    member _.``Node click followed by background miss leaves no node selected``() =
        let node = spawnNode world

        node |> handleClick world
        world |> handlePointerMissed
        selectNodes world |> ignore

        node |> has Selected =! false

    [<Fact>]
    member _.``Select-mode button click followed by node click applies both in raise order``() =
        let button = world |> buttonWithLabel "Multi-select"
        let node = spawnNode world

        button |> handleClick world
        node |> handleClick world
        selectNodes world |> ignore

        node |> has Selected =! true
        buttonLabel button =! "Single-select"

    [<Fact>]
    member _.``Node click followed by select-mode button click applies both in raise order``() =
        let button = world |> buttonWithLabel "Multi-select"
        let node = spawnNode world

        node |> handleClick world
        button |> handleClick world
        selectNodes world |> ignore

        node |> has Selected =! false
        buttonLabel button =! "Single-select"

    [<Fact>]
    member _.``A View-stamped click replaces prior selection when processed in Move mode``() =
        let first = spawnNode world
        let second = spawnNode world

        world |> buttonWithLabel "Multi-select" |> handleClick world
        selectNodes world |> ignore
        cleanupEvents world |> ignore

        first |> add Selected
        world |> enterMode Viewing
        second |> handleClick world
        world |> enterMode Moving
        world |> selectNodes |> ignore

        first |> has Selected =! false
        second |> has Selected =! true

    [<Fact>]
    member _.``A Move-stamped click preserves prior selection when processed in View mode``() =
        let first = spawnNode world
        let second = spawnNode world
        let button = world |> buttonWithLabel "Multi-select"

        button |> handleClick world
        selectNodes world |> ignore
        cleanupEvents world |> ignore

        first |> add Selected
        world |> enterMode Moving
        second |> handleClick world
        world |> enterMode Viewing
        selectNodes world |> ignore

        first |> has Selected =! true
        second |> has Selected =! true

    [<Fact>]
    member _.``Clicking the select-mode button enables two selections in one frame``() =
        let buttonEntity = world |> buttonWithLabel "Multi-select"
        let node1 = spawnNode world
        let node2 = spawnNode world

        buttonEntity |> handleClick world
        node1 |> handleClick world
        node2 |> handleClick world
        selectNodes world |> ignore

        (node1 |> has Selected) =! true
        (node2 |> has Selected) =! true

    [<Fact>]
    member _.``After toggling back to single-select, the last same-frame node click is selected``() =
        let buttonEntity = world |> buttonWithLabel "Multi-select"

        buttonEntity |> handleClick world
        selectNodes world |> ignore
        cleanupEvents world |> ignore
        buttonLabel buttonEntity =! "Single-select"

        buttonEntity |> handleClick world
        selectNodes world |> ignore
        cleanupEvents world |> ignore
        buttonLabel buttonEntity =! "Multi-select"

        let first = spawnNode world
        let second = spawnNode world
        first |> handleClick world
        second |> handleClick world
        selectNodes world |> ignore

        first |> has Selected =! false
        second |> has Selected =! true

    [<Fact>]
    member _.``Clicking button clears selection and updates label``() =
        let node = spawnNode world
        node |> add Selected

        let buttonEntity = world |> buttonWithLabel "Multi-select"
        buttonEntity |> handleClick world

        selectNodes world |> ignore

        (node |> has Selected) =! false

        buttonLabel buttonEntity =! "Single-select"

    [<Fact>]
    member _.``View mode selects the clicked node when nothing is selected``() =
        world |> enterMode Viewing
        let node = spawnNode world
        node |> handleClick world

        selectNodes world |> ignore

        (node |> has Selected) =! true

    [<Fact>]
    member _.``View mode dismisses by deselecting when the selected node is clicked``() =
        world |> enterMode Viewing
        let node = spawnNode world
        node |> add Selected
        node |> handleClick world

        selectNodes world |> ignore

        (node |> has Selected) =! false

    [<Fact>]
    member _.``View mode selects a different node when another node is already selected``() =
        world |> enterMode Viewing
        let selected = spawnNode world
        let other = spawnNode world
        selected |> add Selected
        other |> handleClick world

        selectNodes world |> ignore

        // View mode behaves like single-select mode: the newly-clicked node replaces the
        // previous selection instead of merely dismissing the overlay.
        (selected |> has Selected) =! false
        (other |> has Selected) =! true

    [<Fact>]
    member _.``View mode stays single-select even when the select-mode button is multi-select``() =
        // Toggle the select-mode button to multi-select (only meaningful while in Move mode).
        let buttonEntity = world |> buttonWithLabel "Multi-select"
        buttonEntity |> handleClick world
        selectNodes world |> ignore
        cleanupEvents world |> ignore

        // Enter View mode and select the first node.
        world |> enterMode Viewing
        let node1 = spawnNode world
        node1 |> handleClick world
        selectNodes world |> ignore
        (node1 |> has Selected) =! true
        cleanupEvents world |> ignore

        // Clicking a second node must replace the selection, not add to it, despite multi-select.
        let node2 = spawnNode world
        node2 |> handleClick world
        selectNodes world |> ignore

        let selectedCount = world.Query(With Selected) |> Seq.length
        selectedCount =! 1
        (node1 |> has Selected) =! false
        (node2 |> has Selected) =! true
        cleanupEvents world |> ignore

        // Back in Move mode, the button's multi-select setting must still be in effect: it was
        // only overridden for View mode, never reset.
        world |> enterMode Moving
        let node3 = spawnNode world
        node3 |> handleClick world
        selectNodes world |> ignore

        (node2 |> has Selected) =! true
        (node3 |> has Selected) =! true

    /// A click can no longer reach a hidden control — `Events.handleClick` refuses to raise one —
    /// so `selectNodes` carries no stale-click guard. This pins that the protection really is at
    /// the source: a click raised the way the view layer raises it never arrives.
    [<Fact>]
    member _.``A click on the hidden select-mode button never reaches selectNodes``() =
        let node = spawnNode world
        node |> add Selected

        let buttonEntity = world |> buttonWithLabel "Multi-select"
        let labelBefore = buttonLabel buttonEntity
        // View mode hides the button; the view layer may still deliver one delayed click.
        buttonEntity |> add Hidden
        buttonEntity |> handleClick world

        selectNodes world |> ignore

        node |> has Selected =! true
        buttonLabel buttonEntity =! labelBefore
