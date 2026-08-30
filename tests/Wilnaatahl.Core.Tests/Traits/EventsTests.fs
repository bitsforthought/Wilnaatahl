module Wilnaatahl.Tests.Traits.EventsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Entities
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World
    do world.AddWith CurrentMode Viewing

    /// Raises the signal that a drag is running, marking nothing as taking part in it. These
    /// handlers only ever ask whether a drag is running, never what it moves.
    let beginDrag () = world.Add DragInFlight

    /// Ends the synthetic drag started by `beginDrag`.
    let endDrag () = world.Remove DragInFlight

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``handleClick queues a click on the entity``() =
        let entity = world.Spawn()
        entity |> handleClick world
        world |> inputEvents |> List.ofSeq =! [ Clicked(entity, Viewing) ]

    [<Fact>]
    member _.``handleClick stamps the mode that is live when the click is raised``() =
        let entity = world.Spawn()
        world.Set CurrentMode Moving

        entity |> handleClick world

        world |> inputEvents |> List.ofSeq =! [ Clicked(entity, Moving) ]

    /// The app hides a control one frame before the view layer stops drawing it, so a click can
    /// still land on a control that is no longer available. Dropping it here means the systems
    /// that read clicks never have to check for it.
    [<Fact>]
    member _.``handleClick queues no click on a Hidden entity``() =
        let entity = world.Spawn()
        entity |> add Hidden
        entity |> handleClick world
        entity |> wasClicked world =! false

    /// A click can reach a control during a drag — from a second finger tapping it, for example.
    /// Acting on it would mean carrying out the click and the drag together, so it is refused.
    [<Fact>]
    member _.``handleClick queues no click while a drag is in flight``() =
        let entity = world.Spawn()
        beginDrag ()
        entity |> handleClick world
        entity |> wasClicked world =! false

    /// Input arrives between frames, so a drag start is visible here before the Dragging system
    /// has run and added `DragInFlight`. The frame a drag starts in is part of the drag.
    [<Fact>]
    member _.``handleClick queues no click in the frame a drag starts``() =
        let entity = world.Spawn()
        handleDragStart world
        entity |> handleClick world
        entity |> wasClicked world =! false

    /// Refusing input must stop when the drag does, or the app would accept no input at all after
    /// the first drag.
    [<Fact>]
    member _.``handleClick queues a click once a drag has ended``() =
        let entity = world.Spawn()
        beginDrag ()
        endDrag ()
        entity |> handleClick world
        entity |> wasClicked world =! true

    /// A click belongs to the entity it landed on, so two clicks in one frame stay apart.
    [<Fact>]
    member _.``wasClicked reports only the entities that were clicked``() =
        let clicked = world.Spawn()
        let other = world.Spawn()

        clicked |> handleClick world

        clicked |> wasClicked world =! true
        other |> wasClicked world =! false

    /// Clicks have to come back in the order they were raised, with the input raised between them
    /// left out. The entities are clicked in the opposite order to the one they were spawned in,
    /// so returning them in entity order rather than raise order would not pass.
    [<Fact>]
    member _.``clickedEntities returns the clicked entities in order``() =
        let spawnedFirst = world.Spawn()
        let spawnedSecond = world.Spawn()

        spawnedSecond |> handleClick world
        handleDrag world 1.0 0.0 0.0
        spawnedFirst |> handleClick world

        world |> clickedEntities |> List.ofSeq =! [ spawnedSecond; spawnedFirst ]

    [<Fact>]
    member _.``handleDrag queues the distance travelled``() =
        handleDrag world 1.0 2.0 3.0
        world |> inputEvents |> List.ofSeq =! [ Dragged(Line3.pos 1.0 2.0 3.0) ]

    [<Fact>]
    member _.``handleDragStart queues a drag start``() =
        handleDragStart world
        world |> inputEvents |> List.ofSeq =! [ DragStarted ]

    /// Input is refused from the moment a drag start arrives, but a tap accepted just before that
    /// lands in the same frame. Dropping it stops one frame from applying both a tap and a drag.
    [<Fact>]
    member _.``handleDragStart discards input raised before it``() =
        let entity = world.Spawn()
        entity |> handleClick world
        handlePointerMissed world

        world |> inputEvents |> List.ofSeq
        =! [ Clicked(entity, Viewing); PointerMissed ]

        handleDragStart world

        world |> inputEvents |> List.ofSeq =! [ DragStarted ]

    /// Only clicks and background misses are discarded. A queued release belongs to the drag that
    /// is already running, and dropping it would leave that drag running with no release left to
    /// end it.
    [<Fact>]
    member _.``handleDragStart keeps queued drag input``() =
        beginDrag ()
        handleDrag world 1.0 0.0 0.0
        handleDragEnd world

        handleDragStart world

        world |> inputEvents |> List.ofSeq
        =! [ Dragged(Line3.pos 1.0 0.0 0.0); DragEnded ]

    /// Every background miss is discarded, not just the first. One left in the queue would clear
    /// the selection on the frame the drag starts, leaving the drag moving nodes the view layer
    /// no longer paints as selected.
    [<Fact>]
    member _.``handleDragStart discards every background miss``() =
        handlePointerMissed world
        handleDrag world 1.0 0.0 0.0
        handlePointerMissed world

        handleDragStart world

        world |> inputEvents |> List.ofSeq
        =! [ Dragged(Line3.pos 1.0 0.0 0.0); DragStarted ]

    /// The first drag start decides which nodes the drag moves and where each of them began. A
    /// second start queued before any system has run would record those positions again, after
    /// the drag had already moved the nodes.
    [<Fact>]
    member _.``handleDragStart queues no second drag start before a frame runs``() =
        handleDragStart world
        handleDrag world 1.0 0.0 0.0

        handleDragStart world

        world |> inputEvents |> List.ofSeq
        =! [ DragStarted; Dragged(Line3.pos 1.0 0.0 0.0) ]

    [<Fact>]
    member _.``handleDragEnd queues a drag end``() =
        handleDragEnd world
        world |> inputEvents |> List.ofSeq =! [ DragEnded ]

    [<Fact>]
    member _.``handlePointerMissed queues a background miss``() =
        handlePointerMissed world
        world |> inputEvents |> List.ofSeq =! [ PointerMissed ]

    /// Systems apply a frame's input in the order it was raised, so reading the queue back has to
    /// return that same order.
    [<Fact>]
    member _.``inputEvents returns input in the order it was raised``() =
        handleDragStart world
        handleDrag world 1.0 0.0 0.0
        handleDrag world 2.0 0.0 0.0
        handleDragEnd world

        world |> inputEvents |> List.ofSeq
        =! [
            DragStarted
            Dragged(Line3.pos 1.0 0.0 0.0)
            Dragged(Line3.pos 2.0 0.0 0.0)
            DragEnded
        ]

    /// The first drag start decides which nodes the drag moves and where each of them began.
    /// `Events` refuses a start while a drag is running, so the system never sees a second one.
    [<Fact>]
    member _.``handleDragStart queues no drag start while a drag is in flight``() =
        beginDrag ()
        handleDragStart world
        world |> inputEvents |> Seq.isEmpty =! true

    /// The selection is what the view layer paints and makes draggable, so clearing it mid-drag
    /// would leave the app dragging nodes it no longer shows as selected.
    [<Fact>]
    member _.``handlePointerMissed queues no background miss while a drag is in flight``() =
        beginDrag ()
        handlePointerMissed world
        world |> inputEvents |> Seq.isEmpty =! true

    [<Fact>]
    member _.``cleanupEvents discards the frame's input``() =
        let entity = world.Spawn()
        entity |> handleClick world
        handleDrag world 1.0 0.0 0.0
        handleDragEnd world

        world |> inputEvents |> Seq.isEmpty =! false

        cleanupEvents world |> ignore

        world |> inputEvents |> Seq.isEmpty =! true
        entity |> wasClicked world =! false
