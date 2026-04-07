namespace Wilnaatahl.Tests.ECS

open System
open Wilnaatahl.ECS
open Wilnaatahl.ECS.Entity
open Wilnaatahl.ECS.Extensions
open Wilnaatahl.ECS.Tracking
open Wilnaatahl.ECS.Trait
open Wilnaatahl.Tests.ECS.TestInfra

#if FABLE_COMPILER
open Wilnaatahl.Tests.ECS.FableTestInfra
#else
open Xunit
open Swensen.Unquote
#endif

[<Collection("ECS")>]
type TrackingTests() =
    let wrapper = new TestWorldWrapper()
    let world = wrapper.World
    let IsTagged = tagTrait ()
    let Age = valueTrait {| age = 0 |}
    let Name = valueTrait {| name = "" |}

    interface IDisposable with
        member _.Dispose() = (wrapper :> IDisposable).Dispose()

    [<Fact>]
    member _.``Can create and use trackers``() =
        let added = createAdded ()
        let changed = createChanged ()
        let removed = createRemoved ()
        added.Tracker =! AddedTracker
        changed.Tracker =! ChangedTracker
        removed.Tracker =! RemovedTracker

    // ================================================================
    // A. Added Tracker — Basic Behavior
    // ================================================================

    [<Fact>]
    member _.``A1: Trait added after tracker created, before first query``() =
        let Added = createAdded ()
        let e = world.Spawn [||]
        e |> add Age
        let results = world.Query(Added <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``A2: Trait present at spawn counts as added``() =
        let Added = createAdded ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        let results = world.Query(Added <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``A3: Query drains results (reset after query)``() =
        let Added = createAdded ()
        let _ = world.Spawn [| Age.Val {| age = 0 |} |]
        let first = world.Query(Added <=> [| Age |]) |> Seq.length
        first =! 1
        let second = world.Query(Added <=> [| Age |]) |> Seq.length
        second =! 0

    [<Fact>]
    member _.``A4: New additions after drain are detected``() =
        let Added = createAdded ()
        let _ = world.Spawn [| Age.Val {| age = 0 |} |]
        world.Query(Added <=> [| Age |]) |> ignore // drain
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        let results = world.Query(Added <=> [| Age |]) |> Set.ofSeq
        results =! set [ b ]

    [<Fact>]
    member _.``A5: Pre-existing trait before tracker creation is NOT detected``() =
        let _ = world.Spawn [| Age.Val {| age = 0 |} |]
        let Added = createAdded ()
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``A6: No new addition returns empty``() =
        let Added = createAdded ()
        let _ = world.Spawn [| Age.Val {| age = 0 |} |]
        world.Query(Added <=> [| Age |]) |> ignore // drain
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    // ================================================================
    // B. Added Tracker — Edge Cases
    // ================================================================

    [<Fact>]
    member _.``B1: Trait added then removed before query is NOT returned``() =
        let Added = createAdded ()
        let e = world.Spawn [||]
        e |> add Age
        e |> remove Age
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``B2: Trait removed then added before query IS returned``() =
        let Added = createAdded ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        world.Query(Added <=> [| Age |]) |> ignore // drain
        e |> remove Age
        e |> add Age
        let results = world.Query(Added <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``B3: Adding unrelated trait does not trigger Added for tracked trait``() =
        let Added = createAdded ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        world.Query(Added <=> [| Age |]) |> ignore // drain
        e |> add IsTagged
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``B4: Multi-trait Added drains per-trait — adding second trait after drain does not match``() =
        let Added = createAdded ()
        let e = world.Spawn [||]
        e |> add Age
        let partial = world.Query(Added <=> [| Age; Name |]) |> Seq.length
        partial =! 0
        e |> add Name
        // Age's added status was drained by the previous query, so this still won't match
        let full = world.Query(Added <=> [| Age; Name |]) |> Seq.length
        full =! 0

    [<Fact>]
    member _.``B5: Entity destroyed after being added is NOT returned``() =
        let Added = createAdded ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> destroy
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    // ================================================================
    // C. Added Tracker — Multiple Trackers
    // ================================================================

    [<Fact>]
    member _.``C1: Two Added trackers track independently``() =
        let Added1 = createAdded ()
        let Added2 = createAdded ()
        let a = world.Spawn [| Age.Val {| age = 0 |} |]
        world.Query(Added1 <=> [| Age |]) |> ignore // drain Added1
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        let r1 = world.Query(Added1 <=> [| Age |]) |> Set.ofSeq
        let r2 = world.Query(Added2 <=> [| Age |]) |> Set.ofSeq
        r1 =! set [ b ]
        r2 =! set [ a; b ]

    [<Fact>]
    member _.``C2: Draining one Added tracker does not affect another``() =
        let Added1 = createAdded ()
        let Added2 = createAdded ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        world.Query(Added1 <=> [| Age |]) |> ignore // drain Added1
        let r2 = world.Query(Added2 <=> [| Age |]) |> Set.ofSeq
        r2 =! set [ e ]

    // ================================================================
    // D. Removed Tracker — Basic Behavior
    // ================================================================

    [<Fact>]
    member _.``D1: Trait removed after tracker created``() =
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> remove Age
        let results = world.Query(Removed <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``D2: Removed query drains results``() =
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> remove Age
        let first = world.Query(Removed <=> [| Age |]) |> Seq.length
        first =! 1
        let second = world.Query(Removed <=> [| Age |]) |> Seq.length
        second =! 0

    [<Fact>]
    member _.``D3: New removals after drain are detected``() =
        let Removed = createRemoved ()
        let a = world.Spawn [| Age.Val {| age = 0 |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        a |> remove Age
        world.Query(Removed <=> [| Age |]) |> ignore // drain
        b |> remove Age
        let results = world.Query(Removed <=> [| Age |]) |> Set.ofSeq
        results =! set [ b ]

    [<Fact>]
    member _.``D4: Entity destroyed triggers Removed for its traits``() =
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        e |> destroy
        let rAge = world.Query(Removed <=> [| Age |]) |> Set.ofSeq
        rAge =! set [ e ]

    [<Fact>]
    member _.``D5: Removing a trait the entity never had is NOT returned``() =
        let Removed = createRemoved ()
        let e = world.Spawn [||]
        e |> remove Age
        let results = world.Query(Removed <=> [| Age |]) |> Seq.length
        results =! 0

    // ================================================================
    // E. Removed Tracker — Edge Cases
    // ================================================================

    [<Fact>]
    member _.``E1: Trait added and removed in same frame IS returned``() =
        let Removed = createRemoved ()
        let e = world.Spawn [||]
        e |> add Age
        e |> remove Age
        let results = world.Query(Removed <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``E2: Trait removed then re-added cancels removal``() =
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> remove Age
        e |> add Age
        let results = world.Query(Removed <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``E3: Multi-trait Removed drains per-trait``() =
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        e |> remove Age
        let partial = world.Query(Removed <=> [| Age; Name |]) |> Seq.length
        partial =! 0
        e |> remove Name
        // Age's removed status was drained by the previous query
        let full = world.Query(Removed <=> [| Age; Name |]) |> Seq.length
        full =! 0

    [<Fact>]
    member _.``E4: Destroyed entity appears in Removed results``() =
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> destroy
        let results = world.Query(Removed <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    // ================================================================
    // F. Removed Tracker — Multiple Trackers
    // ================================================================

    [<Fact>]
    member _.``F1: Two Removed trackers track independently``() =
        let Removed1 = createRemoved ()
        let Removed2 = createRemoved ()
        let a = world.Spawn [| Age.Val {| age = 0 |} |]
        a |> remove Age
        world.Query(Removed1 <=> [| Age |]) |> ignore // drain Removed1
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        b |> remove Age
        let r1 = world.Query(Removed1 <=> [| Age |]) |> Set.ofSeq
        let r2 = world.Query(Removed2 <=> [| Age |]) |> Set.ofSeq
        r1 =! set [ b ]
        r2 =! set [ a; b ]

    // ================================================================
    // G. Changed Tracker — Basic Behavior
    // ================================================================

    [<Fact>]
    member _.``G1: entity set triggers Changed``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 99 |}
        let results = world.Query(Changed <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``G3: Adding a trait does NOT trigger Changed``() =
        let Changed = createChanged ()
        let e = world.Spawn [||]
        e |> add Age
        let results = world.Query(Changed <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``G4: Changed query drains results``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 99 |}
        let first = world.Query(Changed <=> [| Age |]) |> Seq.length
        first =! 1
        let second = world.Query(Changed <=> [| Age |]) |> Seq.length
        second =! 0

    [<Fact>]
    member _.``G5: New changes after drain are detected``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 99 |}
        world.Query(Changed <=> [| Age |]) |> ignore // drain
        e |> setValue Age {| age = 100 |}
        let results = world.Query(Changed <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``G6: entity set to same value still triggers Changed``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 0 |}
        let results = world.Query(Changed <=> [| Age |]) |> Seq.length
        results =! 1

    // ================================================================
    // H. Changed Tracker — Edge Cases
    // ================================================================

    [<Fact>]
    member _.``H1: Changed then trait removed — entity IS still returned``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 99 |}
        e |> remove Age
        let results = world.Query(Changed <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``H3: Multiple changes between queries — entity returned once``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 10 |}
        e |> setValue Age {| age = 20 |}
        e |> setValue Age {| age = 30 |}
        let results = world.Query(Changed <=> [| Age |]) |> Seq.length
        results =! 1

    [<Fact>]
    member _.``H4: Multi-trait Changed drains per-trait``() =
        let Changed = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        e |> setValue Age {| age = 1 |}
        let partial = world.Query(Changed <=> [| Age; Name |]) |> Seq.length
        partial =! 0
        e |> setValue Name {| name = "x" |}
        // Age's changed status was drained
        let full = world.Query(Changed <=> [| Age; Name |]) |> Seq.length
        full =! 0

    [<Fact>]
    member _.``H5: Spawn with initial value does NOT trigger Changed``() =
        let Changed = createChanged ()
        let _ = world.Spawn [| Age.Val {| age = 50 |} |]
        let results = world.Query(Changed <=> [| Age |]) |> Seq.length
        results =! 0

    // ================================================================
    // I. Changed Tracker — Multiple Trackers
    // ================================================================

    [<Fact>]
    member _.``I1: Two Changed trackers track independently``() =
        let Changed1 = createChanged ()
        let Changed2 = createChanged ()
        let a = world.Spawn [| Age.Val {| age = 0 |} |]
        a |> setValue Age {| age = 99 |}
        world.Query(Changed1 <=> [| Age |]) |> ignore // drain Changed1
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        b |> setValue Age {| age = 100 |}
        let r1 = world.Query(Changed1 <=> [| Age |]) |> Set.ofSeq
        let r2 = world.Query(Changed2 <=> [| Age |]) |> Set.ofSeq
        r1 =! set [ b ]
        r2 =! set [ a; b ]

    [<Fact>]
    member _.``I2: Draining one Changed tracker does not affect another``() =
        let Changed1 = createChanged ()
        let Changed2 = createChanged ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 99 |}
        world.Query(Changed1 <=> [| Age |]) |> ignore // drain Changed1
        let r2 = world.Query(Changed2 <=> [| Age |]) |> Set.ofSeq
        r2 =! set [ e ]

    // ================================================================
    // J. UpdateEachWith Change Detection
    // ================================================================

    [<Fact>]
    member _.``J1: AutoTrack detects changes when Changed modifier is on the same query``() =
        let Changed = createChanged ()
        let Mutable = mutableTrait {| X = 0 |} { X = 0 }
        let e = world.Spawn [| Mutable.Val {| X = 0 |} |]
        e |> setValue Mutable {| X = 10 |}
        world.QueryTrait(Mutable, Changed <=> [| Mutable |]).UpdateEachWith AutoTrack (fun (m, _) -> m.X <- 42)
        let results = world.Query(Changed <=> [| Mutable |]) |> Seq.length
        results =! 1

    [<Fact>]
    member _.``J2: NeverTrack does not trigger Changed``() =
        let Changed = createChanged ()
        let Mutable = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| Mutable.Val {| X = 0 |} |]
        world.QueryTrait(Mutable).UpdateEachWith NeverTrack (fun (m, _) -> m.X <- 42)
        let results = world.Query(Changed <=> [| Mutable |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``J3: AlwaysTrack triggers Changed when value changes``() =
        let Changed = createChanged ()
        let Mutable = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| Mutable.Val {| X = 0 |} |]
        world.QueryTrait(Mutable).UpdateEachWith AlwaysTrack (fun (m, _) -> m.X <- 42)
        let results = world.Query(Changed <=> [| Mutable |]) |> Seq.length
        results =! 1

    [<Fact>]
    member _.``J4: AlwaysTrack shallow comparison: no actual change → NOT detected``() =
        let Changed = createChanged ()
        let Mutable = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| Mutable.Val {| X = 0 |} |]
        world.QueryTrait(Mutable).UpdateEachWith AlwaysTrack (fun (m, _) -> m.X <- 0)
        let results = world.Query(Changed <=> [| Mutable |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``J5: AlwaysTrack shallow comparison: actual change → IS detected``() =
        let Changed = createChanged ()
        let Mutable = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| Mutable.Val {| X = 0 |} |]
        world.QueryTrait(Mutable).UpdateEachWith AlwaysTrack (fun (m, _) -> m.X <- 1)
        let results = world.Query(Changed <=> [| Mutable |]) |> Seq.length
        results =! 1

    [<Fact>]
    member _.``J6: QueryTraits AlwaysTrack only notifies changed traits``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |} |]
        world.QueryTraits(T1, T2).UpdateEachWith AlwaysTrack (fun ((m1, _m2), _) -> m1.X <- 42)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 0

    [<Fact>]
    member _.``J7: QueryTraits AlwaysTrack both changed → both notified``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |} |]
        world.QueryTraits(T1, T2).UpdateEachWith AlwaysTrack (fun ((m1, m2), _) ->
            m1.X <- 42
            m2.X <- 99)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 1

    [<Fact>]
    member _.``J8: QueryTraits AutoTrack only tracks traits in Changed modifier``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let e = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |} |]
        e |> setValue T1 {| X = 5 |}
        // AutoTrack on a query with Changed(T1) — only T1 should be tracked
        world.QueryTraits(T1, T2, Changed <=> [| T1 |]).UpdateEachWith AutoTrack (fun ((m1, m2), _) ->
            m1.X <- 42
            m2.X <- 99)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 0

    [<Fact>]
    member _.``J9: QueryTraits3 AlwaysTrack only notifies changed traits``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let T3 = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |}; T3.Val {| X = 0 |} |]
        world.QueryTraits3(T1, T2, T3).UpdateEachWith AlwaysTrack (fun ((m1, _m2, _m3), _) -> m1.X <- 42)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        let changedT3 = world.Query(Changed <=> [| T3 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 0
        changedT3 =! 0

    [<Fact>]
    member _.``J10: QueryTraits4 AlwaysTrack only notifies changed traits``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let T3 = mutableTrait {| X = 0 |} { X = 0 }
        let T4 = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |}; T3.Val {| X = 0 |}; T4.Val {| X = 0 |} |]
        world.QueryTraits4(T1, T2, T3, T4).UpdateEachWith AlwaysTrack (fun ((m1, _m2, _m3, _m4), _) -> m1.X <- 42)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        let changedT3 = world.Query(Changed <=> [| T3 |]) |> Seq.length
        let changedT4 = world.Query(Changed <=> [| T4 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 0
        changedT3 =! 0
        changedT4 =! 0

    [<Fact>]
    member _.``J11: QueryTraits3 AutoTrack only tracks traits in Changed modifier``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let T3 = mutableTrait {| X = 0 |} { X = 0 }
        let e = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |}; T3.Val {| X = 0 |} |]
        e |> setValue T1 {| X = 5 |}
        world.QueryTraits3(T1, T2, T3, Changed <=> [| T1 |]).UpdateEachWith AutoTrack (fun ((m1, m2, m3), _) ->
            m1.X <- 42
            m2.X <- 99
            m3.X <- 77)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        let changedT3 = world.Query(Changed <=> [| T3 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 0
        changedT3 =! 0

    [<Fact>]
    member _.``J12: QueryTraits3 AlwaysTrack notifies 2nd and 3rd traits``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let T3 = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |}; T3.Val {| X = 0 |} |]
        world.QueryTraits3(T1, T2, T3).UpdateEachWith AlwaysTrack (fun ((_m1, m2, m3), _) ->
            m2.X <- 99
            m3.X <- 77)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        let changedT3 = world.Query(Changed <=> [| T3 |]) |> Seq.length
        changedT1 =! 0
        changedT2 =! 1
        changedT3 =! 1

    [<Fact>]
    member _.``J13: QueryTraits4 AutoTrack only tracks traits in Changed modifier``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let T3 = mutableTrait {| X = 0 |} { X = 0 }
        let T4 = mutableTrait {| X = 0 |} { X = 0 }
        let e = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |}; T3.Val {| X = 0 |}; T4.Val {| X = 0 |} |]
        e |> setValue T1 {| X = 5 |}
        world.QueryTraits4(T1, T2, T3, T4, Changed <=> [| T1 |]).UpdateEachWith AutoTrack (fun ((m1, m2, m3, m4), _) ->
            m1.X <- 42
            m2.X <- 99
            m3.X <- 77
            m4.X <- 55)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        let changedT3 = world.Query(Changed <=> [| T3 |]) |> Seq.length
        let changedT4 = world.Query(Changed <=> [| T4 |]) |> Seq.length
        changedT1 =! 1
        changedT2 =! 0
        changedT3 =! 0
        changedT4 =! 0

    [<Fact>]
    member _.``J14: QueryTraits4 AlwaysTrack notifies 2nd, 3rd, and 4th traits``() =
        let Changed = createChanged ()
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let T3 = mutableTrait {| X = 0 |} { X = 0 }
        let T4 = mutableTrait {| X = 0 |} { X = 0 }
        let _ = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |}; T3.Val {| X = 0 |}; T4.Val {| X = 0 |} |]
        world.QueryTraits4(T1, T2, T3, T4).UpdateEachWith AlwaysTrack (fun ((_m1, m2, m3, m4), _) ->
            m2.X <- 99
            m3.X <- 77
            m4.X <- 55)
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        let changedT2 = world.Query(Changed <=> [| T2 |]) |> Seq.length
        let changedT3 = world.Query(Changed <=> [| T3 |]) |> Seq.length
        let changedT4 = world.Query(Changed <=> [| T4 |]) |> Seq.length
        changedT1 =! 0
        changedT2 =! 1
        changedT3 =! 1
        changedT4 =! 1

    [<Fact>]
    member _.``J13: Removing a queried trait during UpdateEachWith does not crash``() =
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let e = world.Spawn [| T1.Val {| X = 0 |}; T2.Val {| X = 0 |} |]
        // The callback removes T2 from the entity during iteration.
        // UpdateEachWith should not crash even though T2 is gone when it
        // tries to do the after-read for change detection.
        world.QueryTraits(T1, T2).UpdateEachWith AlwaysTrack (fun ((m, _), entity) ->
            m.X <- 42
            entity |> remove T2)
        let val1 = (e |> get T1).Value
        val1.X =! 42
        (e |> has T2) =! false

    [<Fact>]
    member _.``J14: Removing a queried value trait during UpdateEachWith does not crash``() =
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let T2 = mutableTrait {| X = 0 |} { X = 0 }
        let Changed = createChanged ()
        let e = world.Spawn [| T1.Val {| X = 5 |}; T2.Val {| X = 10 |} |]
        // Remove T2 inside the callback — should not crash during the after-read.
        world.QueryTraits(T1, T2).UpdateEachWith AlwaysTrack (fun ((m1, _), entity) ->
            m1.X <- 99
            entity |> remove T2)
        let val1 = (e |> get T1).Value
        val1.X =! 99
        (e |> has T2) =! false
        // T1 was changed, so the changed tracker should pick it up.
        let changedT1 = world.Query(Changed <=> [| T1 |]) |> Seq.length
        changedT1 =! 1

    [<Fact>]
    member _.``J15: ForEach reads entity even after queried trait is removed``() =
        let T1 = mutableTrait {| X = 0 |} { X = 0 }
        let Changed = createChanged ()
        let e = world.Spawn [| T1.Val {| X = 5 |} |]
        // Trigger a change so the tracker flags the entity.
        e |> setValue T1 {| X = 10 |}
        // Build the query result — the entity is in the result set.
        let results = world.QueryTrait(T1, Changed <=> [| T1 |])
        // Remove the trait before iterating.
        e |> remove T1
        // Koota still iterates the entity (it reads from a snapshot).
        let mutable count = 0
        results.ForEach(fun _ -> count <- count + 1)
        count =! 1

    // ================================================================
    // K. Modifier Combinations
    // ================================================================

    [<Fact>]
    member _.``K1: Added combined with Not``() =
        let Added = createAdded ()
        let a = world.Spawn [||]
        a |> add Age
        let r1 = world.Query(Added <=> [| Age |], Not [| IsTagged |]) |> Seq.length
        r1 =! 1
        let b = world.Spawn [||]
        b |> add Age
        b |> add IsTagged
        let r2 = world.Query(Added <=> [| Age |], Not [| IsTagged |]) |> Seq.length
        r2 =! 0

    [<Fact>]
    member _.``K2: Removed combined with Not``() =
        let Removed = createRemoved ()
        let a = world.Spawn [| Age.Val {| age = 0 |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        b |> add IsTagged
        a |> remove Age
        let r1 = world.Query(Removed <=> [| Age |], Not [| IsTagged |]) |> Set.ofSeq
        r1 =! set [ a ]
        b |> remove Age
        let r2 = world.Query(Removed <=> [| Age |], Not [| IsTagged |]) |> Seq.length
        r2 =! 0

    [<Fact>]
    member _.``K4: Added and Removed on different traits in same query``() =
        let Added = createAdded ()
        let Removed = createRemoved ()
        let e = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        // drain initial state
        world.Query(Added <=> [| Age |]) |> ignore
        world.Query(Removed <=> [| Name |]) |> ignore
        e |> remove Age
        e |> add Age // re-add
        e |> remove Name
        let results = world.Query(Added <=> [| Age |], Removed <=> [| Name |]) |> Set.ofSeq
        results =! set [ e ]

    // ================================================================
    // L. Tracker Lifecycle / Timing
    // ================================================================

    [<Fact>]
    member _.``L1: Tracker created before any entities exist``() =
        let Added = createAdded ()
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        let results = world.Query(Added <=> [| Age |]) |> Set.ofSeq
        results =! set [ e ]

    [<Fact>]
    member _.``L2: Tracker created after entities exist — snapshot excludes pre-existing``() =
        let _ = world.Spawn [| Age.Val {| age = 0 |} |]
        let Added = createAdded ()
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``L3: Late Removed tracker misses prior removals``() =
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> remove Age
        let Removed = createRemoved ()
        let results = world.Query(Removed <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``L4: Late Changed tracker misses prior changes``() =
        let e = world.Spawn [| Age.Val {| age = 0 |} |]
        e |> setValue Age {| age = 99 |}
        let Changed = createChanged ()
        let results = world.Query(Changed <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``L5: Multiple queries with same tracker in same frame``() =
        let Added = createAdded ()
        let _ = world.Spawn [| Age.Val {| age = 0 |} |]
        let r1 = world.Query(Added <=> [| Age |]) |> Seq.length
        r1 =! 1
        let r2 = world.Query(Added <=> [| Age |]) |> Seq.length
        r2 =! 0
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        let r3 = world.Query(Added <=> [| Age |]) |> Set.ofSeq
        r3 =! set [ b ]

    // ================================================================
    // M. Tracking combined with Or and With operators
    //
    // M1-M3 test Koota's initial population behavior, which skips With/Or filters
    // for tracking queries. This is a known Koota bug:
    // https://github.com/pmndrs/koota/issues/241
    // We intentionally match this behavior so tests are consistent across the
    // mock and real Koota. M4-M5 test the event-driven path (after first drain),
    // which correctly applies With/Or filters.
    // ================================================================

    [<Fact>]
    member _.``M1: Tracking + Or initial population skips Or filter``() =
        let Added = createAdded ()
        let a = world.Spawn [| Age.Val {| age = 0 |} |] // has Added(Age), no Or match
        let _ = world.Spawn [| Name.Val {| name = "" |} |] // no Added(Age)
        let c = world.Spawn [||]
        c |> add IsTagged // no Added(Age)
        let results = world.Query(Added <=> [| Age |], Or [| Name; IsTagged |]) |> Set.ofSeq
        // Initial population: only checks tracking bitmasks, skips Or.
        // Entity A has Added(Age), so it's included despite no Or match.
        results =! set [ a ]

    [<Fact>]
    member _.``M2: Tracking + With initial population skips With filter``() =
        let Added = createAdded ()
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |] // has Added(Age) but no Name
        let results = world.Query(Added <=> [| Age |], With Name) |> Set.ofSeq
        // Initial population: skips With. Both have Added(Age).
        results =! set [ a; b ]

    [<Fact>]
    member _.``M3: Tracking + With + Or initial population skips both filters``() =
        let Added = createAdded ()
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |}; IsTagged.Tag() |]
        let b = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let c = world.Spawn [| Age.Val {| age = 0 |}; IsTagged.Tag() |]
        let d = world.Spawn [| Age.Val {| age = 0 |} |]
        let results = world.Query(Added <=> [| Age |], With Name, Or [| IsTagged |]) |> Set.ofSeq
        // Initial population: skips With and Or. All four have Added(Age).
        results =! set [ a; b; c; d ]

    [<Fact>]
    member _.``M4: After drain, event-driven Added + With filters correctly``() =
        let Added = createAdded ()
        // Drain initial state
        world.Query(Added <=> [| Age |], With Name) |> ignore
        // Now add entities — these go through the event-driven path
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let _ = world.Spawn [| Age.Val {| age = 0 |} |] // has Age but no Name
        let results = world.Query(Added <=> [| Age |], With Name) |> Set.ofSeq
        // Event-driven path should check both tracking AND With
        results =! set [ a ]

    [<Fact>]
    member _.``M5: After drain, event-driven Added + Or filters correctly``() =
        let Added = createAdded ()
        // Drain initial state
        world.Query(Added <=> [| Age |], Or [| Name; IsTagged |]) |> ignore
        // Now add entities
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |] // Age + Name
        let _ = world.Spawn [| Age.Val {| age = 0 |} |] // Age only, no Or match
        let results = world.Query(Added <=> [| Age |], Or [| Name; IsTagged |]) |> Set.ofSeq
        results =! set [ a ]

    [<Fact>]
    member _.``M6: Removed + With initial population skips With filter``() =
        let Removed = createRemoved ()
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |] // has Age but no Name
        a |> remove Age
        b |> remove Age
        let results = world.Query(Removed <=> [| Age |], With Name) |> Set.ofSeq
        // Initial population: skips With. Both have Removed(Age).
        results =! set [ a; b ]

    [<Fact>]
    member _.``M7: After drain, event-driven Removed + With filters correctly``() =
        let Removed = createRemoved ()
        // Drain initial state
        world.Query(Removed <=> [| Age |], With Name) |> ignore
        // Event-driven path
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        a |> remove Age
        b |> remove Age
        let results = world.Query(Removed <=> [| Age |], With Name) |> Set.ofSeq
        // Event-driven path: applies With. Only A has Removed(Age) AND Name.
        results =! set [ a ]

    [<Fact>]
    member _.``M8: Changed + With initial population skips With filter``() =
        let Changed = createChanged ()
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |] // has Age but no Name
        a |> setValue Age {| age = 99 |}
        b |> setValue Age {| age = 99 |}
        let results = world.Query(Changed <=> [| Age |], With Name) |> Set.ofSeq
        // Initial population: skips With. Both have Changed(Age).
        results =! set [ a; b ]

    [<Fact>]
    member _.``M9: After drain, event-driven Changed + With filters correctly``() =
        let Changed = createChanged ()
        // Drain initial state
        world.Query(Changed <=> [| Age |], With Name) |> ignore
        // Event-driven path
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |]
        let b = world.Spawn [| Age.Val {| age = 0 |} |]
        a |> setValue Age {| age = 99 |}
        b |> setValue Age {| age = 99 |}
        let results = world.Query(Changed <=> [| Age |], With Name) |> Set.ofSeq
        // Event-driven path: applies With. Only A has Changed(Age) AND Name.
        results =! set [ a ]

    [<Fact>]
    member _.``M10: After drain, event-driven Added + With + Or filters correctly``() =
        let Added = createAdded ()
        // Drain initial state
        world.Query(Added <=> [| Age |], With Name, Or [| IsTagged |]) |> ignore
        // Event-driven path
        let a = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |}; IsTagged.Tag() |]
        let _ = world.Spawn [| Age.Val {| age = 0 |}; Name.Val {| name = "" |} |] // no Or match
        let _ = world.Spawn [| Age.Val {| age = 0 |}; IsTagged.Tag() |] // no With match
        let _ = world.Spawn [| Age.Val {| age = 0 |} |] // neither
        let results = world.Query(Added <=> [| Age |], With Name, Or [| IsTagged |]) |> Set.ofSeq
        // Must have Added(Age) AND Name AND IsTagged. Only A has all three.
        results =! set [ a ]

    // ================================================================
    // N. Cross-world isolation
    // ================================================================

    [<Fact>]
    member _.``N1: Trackers do not leak events across worlds``() =
        // Create a tracker in THIS world's context
        let Added = createAdded ()

        // Create a SECOND world and spawn entities in it
        use wrapper2 = new TestWorldWrapper()
        let world2 = wrapper2.World
        let _ = world2.Spawn [| Age.Val {| age = 42 |} |]

        // The Added query on THIS world should NOT see entities from world2
        let results = world.Query(Added <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``N2: Removed tracker does not leak destroyed entities across worlds``() =
        let Removed = createRemoved ()

        use wrapper2 = new TestWorldWrapper()
        let world2 = wrapper2.World
        let otherEntity = world2.Spawn [| Age.Val {| age = 42 |} |]
        otherEntity |> destroy

        // Removed query on THIS world should NOT see entities destroyed in world2
        let results = world.Query(Removed <=> [| Age |]) |> Seq.length
        results =! 0

    [<Fact>]
    member _.``N3: Changed tracker does not leak changes across worlds``() =
        let Changed = createChanged ()

        use wrapper2 = new TestWorldWrapper()
        let world2 = wrapper2.World
        let otherEntity = world2.Spawn [| Age.Val {| age = 0 |} |]
        otherEntity |> setValue Age {| age = 99 |}

        // Changed query on THIS world should NOT see changes from world2
        let results = world.Query(Changed <=> [| Age |]) |> Seq.length
        results =! 0
