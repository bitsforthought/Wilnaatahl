module Wilnaatahl.Tests.Entities.ConnectorsTests

open System
open Xunit
open Swensen.Unquote
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.Model
open Wilnaatahl.Model.FamilyGraph
open Wilnaatahl.Entities
open Wilnaatahl.Traits.ConnectorTraits
open Wilnaatahl.Traits.PeopleTraits
open Wilnaatahl.Traits.SpaceTraits
open Wilnaatahl.Tests.EcsTestSupport
open Wilnaatahl.Tests.TestData

let private spawnTestScene (world: IWorld) =
    let graph = createFamilyGraph testPeopleAndParents testCouples
    let wilpId = world |> People.spawnWilpBox testWilpName

    for person, _ in testPeopleAndParents do
        world |> People.spawnTreeNode person wilpId

    graph

type Tests() =
    let ecs = new EcsWorld()
    let world = ecs.World

    interface IDisposable with
        member _.Dispose() = (ecs :> IDisposable).Dispose()

    [<Fact>]
    member _.``spawnAllConnectors creates connector entities for a family``() =
        let graph = spawnTestScene world
        world |> Connectors.spawnAllConnectors graph
        let lineCount = world.Query(With Line) |> Seq.length
        lineCount >! 0

    [<Fact>]
    member _.``spawnAllConnectors creates elbow entities``() =
        let graph = spawnTestScene world
        world |> Connectors.spawnAllConnectors graph
        // With 3 children, we expect at least 1 branch elbow + 3 child junction elbows = 4.
        let elbowCount = world.Query(With Elbow) |> Seq.length
        elbowCount >=! 4

    [<Fact>]
    member _.``spawnAllConnectors spawns spouse bar but no elbow or branch for a childless Couple``() =
        // Self-contained scene: one Wilp parent + one outsider + one childless Couple.
        // After spawnAllConnectors we expect exactly the spouse bar (1 hidden line +
        // 2 parallel lines = 3 Line entities) and zero Elbow entities. Procreative
        // Couples are exercised by the other tests in this fixture.
        let testWilpName = WilpName "X"

        let mWilp = {
            Person.Empty with
                Id = PersonId 700
                Kinship = Wilp { Name = testWilpName; Pdeek = Giskaast }
                Shape = Sphere
        }

        let pPartner = { Person.Empty with Id = PersonId 701; Shape = Cube }

        let childlessCouple = Couple.create (CoupleId 800) mWilp.Id pPartner.Id None
        let people = [ mWilp, None; pPartner, None ]
        let couples = [ childlessCouple ]
        let graph = createFamilyGraph people couples

        let wilpId = world |> People.spawnWilpBox testWilpName

        for person, _ in people do
            world |> People.spawnTreeNode person wilpId

        world |> Connectors.spawnAllConnectors graph

        let lineCount = world.Query(With Line) |> Seq.length
        lineCount =! 3

        let elbowCount = world.Query(With Elbow) |> Seq.length
        elbowCount =! 0
