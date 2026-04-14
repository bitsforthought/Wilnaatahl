module Wilnaatahl.Tests.Systems.DraggingTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.ViewModel.Vector
open Wilnaatahl.Entities
open Wilnaatahl.Traits.Events
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.Systems.Dragging
open Wilnaatahl.Tests.EcsTestSupport

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    [<Fact>]
    member _.``dragNodes with no events returns world unchanged``() =
        let entity = world.Spawn(Position.Val {| x = 5.0; y = 0.0; z = 0.0 |})

        dragNodes world |> ignore

        let pos = (entity |> get Position).Value
        pos.x =! 5.0

    [<Fact>]
    member _.``full drag flow moves selected entity position``() =
        let node =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        // Step 1: Touch the node (PointerDown)
        node |> add PointerDownEvent
        dragNodes world |> ignore

        // Step 2: Start the drag
        world.Add DragStartEvent
        dragNodes world |> ignore

        // Step 3: Drag to new position
        // origin = node position at drag start = {5,0,0}
        // DragEvent move = {7,0,0}
        // delta = origin + move - oldPosition = {5,0,0} + {7,0,0} - {5,0,0} = {7,0,0}
        // new position = {5,0,0} + {7,0,0} = {12,0,0}
        world.AddWith DragEvent {| x = 7.0; y = 0.0; z = 0.0 |}
        dragNodes world |> ignore

        let pos = (node |> get Position).Value
        pos =! Line3.pos 12.0 0.0 0.0

    [<Fact>]
    member _.``sequential drag events accumulate correctly``() =
        let node =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = 5.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        // Touch + drag start
        node |> add PointerDownEvent
        dragNodes world |> ignore
        world.Add DragStartEvent
        dragNodes world |> ignore

        // First drag: origin = {5,0,0}, move = {3,0,0}
        // delta = origin + move - oldPosition = {5,0,0} + {3,0,0} - {5,0,0} = {3,0,0}
        // new position = {5,0,0} + {3,0,0} = {8,0,0}
        world.AddWith DragEvent {| x = 3.0; y = 0.0; z = 0.0 |}
        dragNodes world |> ignore

        let pos1 = (node |> get Position).Value
        pos1 =! Line3.pos 8.0 0.0 0.0

        // Second drag (no drag end in between): origin = {5,0,0}, move = {7,0,0}
        // delta = origin + move - oldPosition = {5,0,0} + {7,0,0} - {8,0,0} = {4,0,0}
        // new position = {8,0,0} + {4,0,0} = {12,0,0}
        world.AddWith DragEvent {| x = 7.0; y = 0.0; z = 0.0 |}
        dragNodes world |> ignore

        let pos2 = (node |> get Position).Value
        pos2 =! Line3.pos 12.0 0.0 0.0

    [<Fact>]
    member _.``drag end cleans up click events``() =
        let node =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = 0.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        // Touch + drag start
        node |> add PointerDownEvent
        dragNodes world |> ignore
        world.Add DragStartEvent
        dragNodes world |> ignore

        // During drag end, there's still a DragEvent in the same frame
        node |> add ClickEvent
        world.AddWith DragEvent {| x = 0.0; y = 0.0; z = 0.0 |}
        world.Add DragEndEvent
        dragNodes world |> ignore

        node |> has ClickEvent =! false

    [<Fact>]
    member _.``spurious drag end without active drag removes DragEndEvent``() =
        world.Add DragEndEvent
        world.Has DragEndEvent =! true

        dragNodes world |> ignore

        world.Has DragEndEvent =! false

    [<Fact>]
    member _.``drag moves multiple selected entities``() =
        let node1 =
            world.Spawn(PersonRef.Val Person.Empty, Position.Val {| x = 0.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        let node2 =
            world.Spawn(Position.Val {| x = 10.0; y = 0.0; z = 0.0 |}, Selected.Tag())

        // Touch first node, start drag, then drag
        node1 |> add PointerDownEvent
        dragNodes world |> ignore
        world.Add DragStartEvent
        dragNodes world |> ignore
        // origin = {0,0,0}, move = {3,0,0}, delta = {0,0,0}+{3,0,0}-{0,0,0} = {3,0,0}
        world.AddWith DragEvent {| x = 3.0; y = 0.0; z = 0.0 |}
        dragNodes world |> ignore

        let pos1 = (node1 |> get Position).Value
        let pos2 = (node2 |> get Position).Value
        pos1.x =! 3.0
        pos2.x =! 13.0

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()
