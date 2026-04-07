module Wilnaatahl.Tests.Systems.SelectionTests

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

[<Fact>]
let ``spawnSelectControls creates button entity`` () =
    use ecs = new EcsWorld()
    let world = ecs.World

    let sortOrder, _ = spawnSelectControls (0, world)

    sortOrder =! 1

    let buttons = world.Query(With Button) |> Seq.toList
    buttons.Length =! 1

[<Fact>]
let ``clicking node in single-select mode selects it`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnSelectControls (0, world) |> ignore

    let node = spawnNode world
    node |> add ClickEvent

    selectNodes world |> ignore

    test <@ node |> has Selected @>

[<Fact>]
let ``clicking selected node deselects it`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnSelectControls (0, world) |> ignore

    let node = spawnNode world
    node |> add Selected
    node |> add ClickEvent

    selectNodes world |> ignore

    test <@ not (node |> has Selected) @>

[<Fact>]
let ``single-select mode clears previous selection on new click`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnSelectControls (0, world) |> ignore

    let node1 = spawnNode world
    let node2 = spawnNode world
    node1 |> add Selected
    node2 |> add ClickEvent

    selectNodes world |> ignore

    test <@ not (node1 |> has Selected) @>
    test <@ node2 |> has Selected @>

[<Fact>]
let ``background click deselects all`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnSelectControls (0, world) |> ignore

    let node = spawnNode world
    node |> add Selected
    world.Add PointerMissedEvent

    selectNodes world |> ignore

    test <@ not (node |> has Selected) @>

[<Fact>]
let ``clicking select mode button toggles multi-select`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnSelectControls (0, world) |> ignore

    // Toggle to multi-select by clicking the button
    let buttonEntity = world.Query(With Button) |> Seq.head
    buttonEntity |> add ClickEvent
    selectNodes world |> ignore
    cleanupEvents world |> ignore

    // Now in multi-select mode: select first node
    let node1 = spawnNode world
    node1 |> add ClickEvent
    selectNodes world |> ignore
    test <@ node1 |> has Selected @>
    cleanupEvents world |> ignore

    // Click second node — first should remain selected
    let node2 = spawnNode world
    node2 |> add ClickEvent
    selectNodes world |> ignore

    test <@ node1 |> has Selected @>
    test <@ node2 |> has Selected @>

[<Fact>]
let ``clicking button clears selection and updates label`` () =
    use ecs = new EcsWorld()
    let world = ecs.World
    spawnSelectControls (0, world) |> ignore

    let node = spawnNode world
    node |> add Selected

    let buttonEntity = world.Query(With Button) |> Seq.head
    buttonEntity |> add ClickEvent

    selectNodes world |> ignore

    test <@ not (node |> has Selected) @>

    let buttonData = (buttonEntity |> get Button).Value
    buttonData.label =! "Single-select"
