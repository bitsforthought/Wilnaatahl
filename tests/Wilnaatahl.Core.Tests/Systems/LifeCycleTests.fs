module Wilnaatahl.Tests.Systems.LifeCycleTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Relation
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Traits.ViewTraits
open Wilnaatahl.ViewModel
open Wilnaatahl.Entities
open Wilnaatahl.System.Layout
open Wilnaatahl.Systems.LifeCycle
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

let private labelOf entity = (entity |> get NodeLabel).Value

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    let labelFor (person: Person) =
        world.Query(With PersonRef)
        |> Seq.find (fun entity -> (entity |> get PersonRef).Value.Id = person.Id)
        |> labelOf

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnControls creates button entities``() =
        spawnControls world
        let buttonCount = world.Query(With Button) |> Seq.length
        // Undo, Redo, View/Move mode, Multi-select mode, Open file, and Save buttons
        buttonCount =! 6

    [<Fact>]
    member _.``spawnControls boots into View mode with the Move-mode-only controls hidden``() =
        spawnControls world

        world.Has InViewMode =! true

        world.Query(With Button, With MoveModeOnly)
        |> Seq.forall (fun entity -> entity |> has Hidden)
        =! true

    /// The toolbar renders in sortOrder, so the spawn order is the layout.
    [<Fact>]
    member _.``spawnControls lays the toolbar out with the modal controls left of the mode toggle``() =
        spawnControls world

        world.Query(With Button)
        |> Seq.map (fun entity -> (entity |> get Button).Value)
        |> Seq.sortBy _.sortOrder
        |> Seq.map _.label
        |> List.ofSeq
        =! [ "Undo"; "Redo"; "Multi-select"; "Move"; "Open file…"; "Save" ]

    [<Fact>]
    member _.``spawnControls seeds the active UI Locale at world scope``() =
        spawnControls world
        world.Get CurrentLocale =! Some En

    [<Fact>]
    member _.``spawnScene creates tree nodes and connectors``() =
        let graph = createFamilyGraph testPeopleAndParents testCouples []
        spawnScene world graph
        let personCount = world.Query(With PersonRef) |> Seq.length
        let lineCount = world.Query(With Line) |> Seq.length
        personCount =! 5
        lineCount >! 0

    [<Fact>]
    member _.``spawnScene renders an outside spouse married to two members as two distinct non-crossing nodes``() =
        // The rendered Wilp "MM" has two members, each married to the same outside spouse, who
        // must render as a separate node per marriage so the spouse-bars don't cross at one node.
        let graph = createFamilyGraph multiMarriagePeople multiMarriageCouples []
        spawnScene world graph
        layoutNodes world graph

        let personIdOf entity = (entity |> get PersonRef).Value.Id

        let isSpouseNode entity =
            personIdOf entity = multiMarriageSpouse.Id

        // 1. Two distinct node entities exist for the shared spouse.
        let spouseNodes =
            world.Query(With PersonRef) |> Seq.filter isSpouseNode |> Seq.toList

        spouseNodes.Length =! 2

        // 2. After layout, the two spouse nodes have distinct target positions.
        let spousePositions =
            world.QueryTrait(TargetPosition, With PersonRef).ToSequence()
            |> Seq.filter (fun (_, entity) -> isSpouseNode entity)
            |> Seq.map fst
            |> Seq.toList

        spousePositions.Length =! 2
        (spousePositions |> List.distinct).Length =! 2

        // 3. Each marriage's spouse-bar joins the correct member to the correct per-marriage
        // partner node. Both couples are childless, so each renders as exactly one hidden
        // spouse-bar Line whose two endpoints snap (via SnapToX) to the couple's two nodes.
        // Reconstructing the NodeKey of both endpoints pins the exact pairing, so a swap of
        // the two bars' targets — itself a crossing — would fail this assertion.
        let nodeKeyOf entity = (entity |> get NodeKeyRef).Value

        let barEndpointKeys =
            world.Query(With Line, With Hidden)
            |> Seq.map (fun line ->
                let firstEndpoint, secondEndpoint = line |> Line3.getEndpoints world

                [ firstEndpoint; secondEndpoint ]
                |> List.choose (targetFor SnapToX)
                |> List.map nodeKeyOf
                |> Set.ofList)
            |> Set.ofSeq

        let member1Key = MemberNode multiMarriageMember1.Id
        let member2Key = MemberNode multiMarriageMember2.Id
        let spouseKey1 = PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple1.Id)
        let spouseKey2 = PartnerNode(multiMarriageSpouse.Id, multiMarriageCouple2.Id)

        barEndpointKeys
        =! Set.ofList [ Set.ofList [ member1Key; spouseKey1 ]; Set.ofList [ member2Key; spouseKey2 ] ]

        // 4. Each per-marriage partner node carries the composed label, proving spawnScene
        // composes labels for partner nodes and not only for members. MMSpouse's current Wilp
        // ("MMOut") differs from the rendered one ("MM"), so both nodes carry the Kinship text.
        let spouseLabels = spouseNodes |> List.map labelOf

        let expectedSpouseLabel = {
            NodeLabelView.Empty with
                ColonialName = Some "MMSpouse"
                KinshipParen = Some "MMOut"
        }

        spouseLabels =! [ expectedSpouseLabel; expectedSpouseLabel ]

    [<Fact>]
    member _.``spawnScene composes a NodeLabel whose Kinship line appears only for a person outside the rendered Wilp``
        ()
        =
        let graph = createFamilyGraph testPeopleAndParents testCouples []
        spawnScene world graph

        // The rendered Wilp is "H", its most populous. p0 (Mother) is in Wilp H.
        labelFor p0 =! { NodeLabelView.Empty with ColonialName = Some "Mother" }

        // p3 (Child2) renders in Wilp H but her current Wilp is "L".
        labelFor p3
        =! {
               NodeLabelView.Empty with
                   ColonialName = Some "Child2"
                   KinshipParen = Some "L"
                   Born = Some(FormattedDate(DateOnly(1900, 1, 1)))
           }

    [<Fact>]
    member _.``spawnScene puts a person's most recent held Name in the composed label``() =
        let earlier = { Name = Name "Sparks"; NameDate = on 1950 1 1; NameOrder = None }
        let later = { Name = Name "Tinker"; NameDate = on 1980 1 1; NameOrder = None }

        let graph =
            createFamilyGraph testPeopleAndParents testCouples [ (p0.Id, earlier); (p0.Id, later) ]

        spawnScene world graph

        // p0 (Mother) renders in her own Wilp H, so there is no Kinship line.
        labelFor p0
        =! {
               NodeLabelView.Empty with
                   ColonialName = Some "Mother"
                   MostRecentName = Some "Tinker"
           }

    /// TODO: Import lets two huwilp share a name while carrying different Pdeeḵ. Nothing rejects
    /// it — `Transform` deduplicates huwilp by *id*, so two entries named "H" with different ids
    /// survive as distinct Kinship values. The person keeps their own Pdeeḵ, but the graph's
    /// Huwilp set, `Scene.enumerateHuwilpToRender`, and this comparison all key on `WilpName`
    /// alone, so the two collapse into one rendered Wilp whose Pdeeḵ depends on which member is
    /// looked at. Fix it at import: reject or merge a Wilp name resolving to more than one Pdeeḵ.
    [<Fact>]
    member _.``currentWilpDiffersFromRendered is false only when the current Wilp name matches``() =
        // Pdeek is deliberately different from anything else — the comparison is on name only.
        let matching = {
            Person.Empty with
                Kinship = Wilp { Name = WilpName "H"; Pdeek = LaxGibuu }
        }

        currentWilpDiffersFromRendered (WilpName "H") matching =! false

    [<Fact>]
    member _.``currentWilpDiffersFromRendered is true when the current Wilp name differs``() =
        let other = {
            Person.Empty with
                Kinship = Wilp { Name = WilpName "L"; Pdeek = Ganeda }
        }

        currentWilpDiffersFromRendered (WilpName "H") other =! true

    [<Fact>]
    member _.``currentWilpDiffersFromRendered is true for UnknownWilp and NoneProvided``() =
        let unknownWilp = { Person.Empty with Kinship = UnknownWilp LaxGibuu }
        let noneProvided = { Person.Empty with Kinship = NoneProvided None }
        currentWilpDiffersFromRendered (WilpName "H") unknownWilp =! true
        currentWilpDiffersFromRendered (WilpName "H") noneProvided =! true
