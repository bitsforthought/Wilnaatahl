namespace Wilnaatahl.ECS.Mocks

open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open System.Threading
open Wilnaatahl.ECS

type ITestRelation =
    abstract member Config: RelationConfig
    abstract member TargetToTraitMap: ConcurrentDictionary<int, ITrait>

type private TestTrait(relation, isTag) =
    member _.Relation = relation

    interface ITrait with
        member _.IsTag = isTag

type private TestTagTrait(relation) =
    inherit TestTrait(relation, true)
    interface ITagTrait

type private ITestUntypedValueTrait =
    abstract UnfreezeUntypedValue: value: obj -> obj
    abstract DefaultMutableValue: obj option

type private ITestValueTrait<'T> =
    inherit IValueTrait<'T>
    inherit ITestUntypedValueTrait
    abstract FreezeValue: mutableValue: obj -> 'T
    abstract UnfreezeValue: value: 'T -> obj

type private TestValueTrait<'T, 'TMutable>
    (relation, freeze: 'TMutable -> 'T, unfreeze: 'T -> 'TMutable, defaultValue: 'T option) =
    inherit TestTrait(relation, false)
    interface IMutableValueTrait<'T, 'TMutable>

    interface ITestValueTrait<'T> with
        member _.UnfreezeUntypedValue value = unfreeze (value :?> 'T) :> obj
        member _.FreezeValue mutableValue = freeze (mutableValue :?> 'TMutable)
        member _.UnfreezeValue value = unfreeze value
        member _.DefaultMutableValue = defaultValue |> Option.map (fun v -> unfreeze v :> obj)

type private TestWildcardTrait(relation: ITestRelation) =

    member _.Relation = relation

    interface ITagTrait with
        member _.IsTag = true

type private TestRelation<'TTrait when 'TTrait :> ITrait>(config, createTrait: ITestRelation -> 'TTrait) as this =
    let wildcard = TestWildcardTrait this
    let targetToTraitMap = ConcurrentDictionary<int, ITrait>()

    interface ITestRelation with
        member _.Config = config
        member _.TargetToTraitMap = targetToTraitMap

    interface IRelation<'TTrait> with
        member _.IsTag = typeof<'TTrait> = typeof<ITagTrait>

        member this.WithTarget(entity: EntityId) =
            let (EntityId entityId) = entity

            match targetToTraitMap.TryGetValue entityId with
            | true, someTrait -> someTrait :?> 'TTrait
            | false, _ ->
                let someTrait = createTrait this
                targetToTraitMap.TryAdd(entityId, someTrait) |> ignore // First concurrent add wins.
                someTrait

        member _.Wildcard() = wildcard

type private TestTagRelation(config) =
    inherit TestRelation<ITagTrait>(config, fun relation -> TestTagTrait(Some relation))

type private TestValueRelation<'T, 'TMutable>(config, freeze, unfreeze, defaultValue: 'T) =
    inherit
        TestRelation<IMutableValueTrait<'T, 'TMutable>>(
            config,
            fun relation -> TestValueTrait<'T, 'TMutable>(Some relation, freeze, unfreeze, Some defaultValue)
        )

type private QueryResult<'T, 'TMutable>
    private (entities, getRead, getMutable, notifyChanges, hasChangedModifier, getReadResilient) =

    static member Create
        (
            entities: seq<EntityId>,
            getRead: EntityId -> 'T,
            getMutable: EntityId -> 'TMutable,
            notifyChanges: ChangeDetectionOption -> EntityId -> 'T -> 'T -> unit,
            hasChangedModifier: bool,
            getReadResilient: 'T -> EntityId -> 'T
        ) =
        QueryResult<'T, 'TMutable>(entities, getRead, getMutable, notifyChanges, hasChangedModifier, getReadResilient)

    interface IQueryResult<'T, 'TMutable> with
        member _.ForEach callback =
            for entity in entities do
                callback (getRead entity, entity)

        member _.UpdateEachWith changeDetectionOption callback =
            let detectChanges =
                match changeDetectionOption with
                | AlwaysTrack -> true
                | AutoTrack -> hasChangedModifier
                | NeverTrack -> false

            if detectChanges then
                for e in entities do
                    let before = getRead e
                    callback (getMutable e, e)
                    let after = getReadResilient before e
                    notifyChanges changeDetectionOption e before after
            else
                for e in entities do
                    callback (getMutable e, e)

    interface IEnumerable<EntityId> with
        member _.GetEnumerator() = entities.GetEnumerator()

    interface IEnumerable with
        member _.GetEnumerator() = entities.GetEnumerator()

[<AutoOpen>]
module private Ids =
    let getWorldId entity =
        let (EntityId id) = entity
        id >>> 28 &&& 0xF

    let getLocalId entity =
        let (EntityId id) = entity
        id &&& 0x0FFFFFFF

    let packEntityId worldId localEntityId =
        worldId <<< 28 ||| (localEntityId &&& 0x0FFFFFFF) |> EntityId

/// Tracks per-(entity, trait) events for a single tracker instance.
/// Each tracker has its own independent set of flagged entities, keyed by trait.
type private TestTracker(trackerType: TrackerType) =
    // Used as a concurrent set (only key presence matters; the bool value is unused).
    // .NET has no built-in ConcurrentHashSet, so ConcurrentDictionary is the standard substitute.
    let flags = ConcurrentDictionary<ITrait, ConcurrentDictionary<int, bool>>()
    let drainedWorlds = ConcurrentDictionary<int, bool>()

    member _.Flag(someTrait: ITrait, EntityId entityId) =
        let traitFlags =
            flags.GetOrAdd(someTrait, fun _ -> ConcurrentDictionary<int, bool>())

        traitFlags[entityId] <- true

    member _.Unflag(someTrait: ITrait, EntityId entityId) =
        match flags.TryGetValue someTrait with
        | true, traitFlags -> traitFlags.TryRemove entityId |> ignore
        | false, _ -> ()

    /// Returns true if this tracker has been drained for the given world before.
    /// Koota skips With/Or filters on the first drain (initial population) but applies
    /// them on subsequent drains (event-driven path).
    member _.HasBeenDrained(worldId: int) = drainedWorlds.ContainsKey worldId

    member _.DrainTrait(someTrait: ITrait, worldId: int) : Set<int> =
        drainedWorlds[worldId] <- true

        match flags.TryGetValue someTrait with
        | true, traitFlags ->
            let matching =
                traitFlags.Keys
                |> Seq.filter (fun eid -> (EntityId eid) |> getWorldId = worldId)
                |> Set.ofSeq

            for eid in matching do
                traitFlags.TryRemove eid |> ignore

            matching
        | false, _ -> Set.empty

    interface ITracker with
        member _.Tracker = trackerType

    interface IAddedTracker
    interface IChangedTracker
    interface IRemovedTracker

/// Global registry of all active tracker instances, partitioned by type.
/// When an add/remove/change event occurs, ALL trackers of the corresponding type get notified.
module private TrackerRegistry =
    let private addedTrackers = ConcurrentDictionary<TestTracker, bool>()
    let private removedTrackers = ConcurrentDictionary<TestTracker, bool>()
    let private changedTrackers = ConcurrentDictionary<TestTracker, bool>()

    let register (tracker: TestTracker) =
        match (tracker :> ITracker).Tracker with
        | AddedTracker -> addedTrackers[tracker] <- true
        | RemovedTracker -> removedTrackers[tracker] <- true
        | ChangedTracker -> changedTrackers[tracker] <- true

        tracker

    let notifyAdded someTrait entity =
        for kvp in addedTrackers do
            kvp.Key.Flag(someTrait, entity)

    let notifyRemoved someTrait entity =
        for kvp in removedTrackers do
            kvp.Key.Flag(someTrait, entity)

    let cancelRemoved someTrait entity =
        for kvp in removedTrackers do
            kvp.Key.Unflag(someTrait, entity)

    let cancelAdded someTrait entity =
        for kvp in addedTrackers do
            kvp.Key.Unflag(someTrait, entity)

    let notifyChanged someTrait entity =
        for kvp in changedTrackers do
            kvp.Key.Flag(someTrait, entity)

type private TestTraitFactory() =
    let findConversionMethods (value: obj) (mutableValue: obj) =
        let flags = BindingFlags.Static ||| BindingFlags.Public
        let mutableType = mutableValue.GetType()
        let immutableType = value.GetType()

        let callConverter name paramType v =
            let methodInfo = mutableType.GetMethod(name, flags, [| paramType |])

            if methodInfo = null then
                v
            else
                let result = (nonNull methodInfo).Invoke(null, [| v |])
                nonNull result

        let freeze = callConverter "FreezeValue" mutableType
        let unfreeze = callConverter "UnfreezeValue" immutableType

        freeze, unfreeze

    interface ITraitFactory with
        member _.CreateAdded() =
            TestTracker(AddedTracker) |> TrackerRegistry.register :> IAddedTracker

        member _.CreateChanged() =
            TestTracker(ChangedTracker) |> TrackerRegistry.register :> IChangedTracker

        member _.CreateRemoved() =
            TestTracker(RemovedTracker) |> TrackerRegistry.register :> IRemovedTracker

        member _.Relation config = TestTagRelation config

        member _.RelationWith(config, store, mutableStore) =
            let freezeUntyped, unfreezeUntyped = findConversionMethods store mutableStore
            let freeze (m: 'TMutable) = freezeUntyped m :?> 'T
            let unfreeze (v: 'T) = unfreezeUntyped v :?> 'TMutable
            TestValueRelation<'T, 'TMutable>(config, freeze, unfreeze, store)

        member _.TagTrait() = TestTagTrait None

        member _.TraitWith value mutableValue =
            let freezeUntyped, unfreezeUntyped = findConversionMethods value mutableValue
            let freeze (m: 'TMutable) = freezeUntyped m :?> 'T
            let unfreeze (v: 'T) = unfreezeUntyped v :?> 'TMutable
            TestValueTrait<'T, 'TMutable>(None, freeze, unfreeze, Some value)

        member _.TraitWithRef _ =
            TestValueTrait<'T, 'T>(None, id, id, None)

[<AutoOpen>]
module private World =
    type World = {
        WorldId: int
        EntityId: EntityId
        TraitStores: ConcurrentDictionary<ITrait, ConcurrentDictionary<int, obj option>>
        mutable NextEntityId: int
    }

    // World ID assignment (top 4 bits).
    let maxWorlds = 16

    let private getStore someTrait world =
        match world.TraitStores.TryGetValue someTrait with
        | true, traitStore -> traitStore
        | false, _ ->
            let newStore = ConcurrentDictionary<int, obj option>()

            world.TraitStores.TryAdd(someTrait, newStore) // First concurrent add wins.
            |> ignore

            newStore

    let private allEntities world =
        world.TraitStores.Values |> Seq.map _.Keys |> Seq.concat |> Set.ofSeq

    let private allocEntity world =
        let entityId = Interlocked.Increment(&world.NextEntityId)
        packEntityId world.WorldId entityId

    let createWorld id = {
        WorldId = id
        EntityId =
            // World-level special entity has ID 0.
            let worldEntityLocalId = 0
            packEntityId id worldEntityLocalId

        TraitStores = ConcurrentDictionary<ITrait, ConcurrentDictionary<int, obj option>>()
        NextEntityId = 0 // First entity will get ID 1 since it's assigned after Interlocked.Increment.
    }

    let hasTrait someTrait (EntityId entityId) world =
        let store = world |> getStore someTrait
        store.ContainsKey entityId

    let private targetsForImpl (relation: ITestRelation) entity world =
        let targetOfSubject (kvp: KeyValuePair<int, ITrait>) =
            if world |> hasTrait kvp.Value entity then
                Some(EntityId kvp.Key)
            else
                None

        relation.TargetToTraitMap |> Seq.choose targetOfSubject |> Array.ofSeq

    let removeTrait someTrait (EntityId entityId) world =
        let store = world |> getStore someTrait
        let existed, _ = store.TryRemove(entityId)

        if existed then
            TrackerRegistry.notifyRemoved someTrait (EntityId entityId)
            TrackerRegistry.cancelAdded someTrait (EntityId entityId)

    let addTrait (someTrait: ITrait) entity world =
        // Deal with exclusive relations. If we're adding a new target for such a relation,
        // we need to clear out the old one first.
        match (someTrait :?> TestTrait).Relation with
        | Some relation when relation.Config.IsExclusive ->
            let currentTargets = world |> targetsForImpl relation entity

            // There should be only one target, but let's be conservative.
            for EntityId targetEntityId in currentTargets do
                let targetTrait = relation.TargetToTraitMap[targetEntityId]
                world |> removeTrait targetTrait entity
        | Some _ -> ()
        | None -> ()

        let (EntityId entityId) = entity
        let store = world |> getStore someTrait

        if store.TryAdd(entityId, None) then
            // Value traits initialize with their schema default (matching Koota's behavior).
            match someTrait with
            | :? ITestUntypedValueTrait as valueTrait ->
                match valueTrait.DefaultMutableValue with
                | Some defaultVal -> store[entityId] <- Some defaultVal
                | None -> ()
            | _ -> ()

            TrackerRegistry.notifyAdded someTrait entity
            TrackerRegistry.cancelRemoved someTrait entity

    let destroy entity world =
        for someTrait in world.TraitStores.Keys do
            world |> removeTrait someTrait entity

    let getTraitValue (valueTrait: IValueTrait<'T>) (EntityId entityId) world =
        let store = world |> getStore valueTrait

        match store.TryGetValue entityId with
        | true, Some value ->
            let testTrait = valueTrait :?> ITestValueTrait<'T>
            Some(value |> testTrait.FreezeValue)
        | _ -> None

    let forceGetTraitValue (valueTrait: IValueTrait<'T>) (EntityId entityId) world =
        let store = world |> getStore valueTrait
        let maybeValue = store[entityId]
        assert maybeValue.IsSome
        maybeValue.Value

    let setTraitValue (valueTrait: IValueTrait<'T>) (value: 'T) (EntityId entityId) world =
        let store = world |> getStore valueTrait

        match store.TryGetValue entityId with
        | true, (_ as oldValue) ->
            let testTrait = valueTrait :?> ITestValueTrait<'T>

            if store.TryUpdate(entityId, Some(value |> testTrait.UnfreezeValue), oldValue) then
                TrackerRegistry.notifyChanged valueTrait (EntityId entityId)
        | false, _ -> invalidArg (nameof valueTrait) $"Trait not present on entity {entityId}"

    let setTraitValueWith (valueTrait: IValueTrait<'T>) (update: 'T -> 'T) (EntityId entityId) world =
        let store = world |> getStore valueTrait

        match store.TryGetValue entityId with
        | true, (Some v as value) ->
            let testTrait = valueTrait :?> ITestValueTrait<'T>
            let newValue = update (v |> testTrait.FreezeValue)

            if store.TryUpdate(entityId, Some(newValue |> testTrait.UnfreezeValue), value) then
                TrackerRegistry.notifyChanged valueTrait (EntityId entityId)
        | _ -> invalidArg (nameof valueTrait) $"Trait value not set on entity {entityId}"

    let targetsFor (relation: IRelation<'TTrait>) entity world =
        let relationImpl = relation :?> TestRelation<'TTrait> :> ITestRelation
        world |> targetsForImpl relationImpl entity

    let targetFor relation entity world =
        let targets = world |> targetsFor relation entity

        if targets |> Seq.isEmpty then
            None
        else
            Some(targets |> Seq.head)

    type private MatchedEntities = {
        With: Set<int> list
        Or: Set<int>
        Not: Set<int>
        /// Each entry is a set of entity IDs that matched ALL traits for a single tracking modifier.
        /// Multiple tracking modifiers are ANDed together.
        Tracking: Set<int> list
        /// True if any tracker in the query is being drained for the first time (initial population).
        /// Koota skips With/Or filters during initial population.
        IsInitialPopulation: bool
    } with

        static member val Empty =
            {
                With = []
                Or = Set.empty
                Not = Set.empty
                Tracking = []
                IsInitialPopulation = false
            }

    let query where world =
        let rec getEntitySet (someTrait: ITrait) =
            match someTrait with
            | :? TestWildcardTrait as t -> t.Relation.TargetToTraitMap.Values |> getEntitySetUnion
            | _ ->
                let store = world |> getStore someTrait
                store.Keys |> Set.ofSeq

        and getEntitySetUnion traits =
            traits |> Seq.map getEntitySet |> Set.unionMany

        let drainForWorld (testTracker: TestTracker) (traits: ITrait[]) =
            let perTrait =
                traits |> Array.map (fun t -> testTracker.DrainTrait(t, world.WorldId))

            if perTrait.Length = 0 then
                Set.empty
            else
                Set.intersectMany perTrait

        let collect acc queryOp =
            match queryOp with
            | With someTrait -> { acc with With = (someTrait |> getEntitySet) :: acc.With }
            | Or traits -> { acc with Or = traits |> getEntitySetUnion |> Set.union acc.Or }
            | Not traits -> { acc with Not = traits |> getEntitySetUnion |> Set.union acc.Not }
            | Added(traits, tracker) ->
                let t = tracker :?> TestTracker
                let wasInitial = not (t.HasBeenDrained world.WorldId)

                {
                    acc with
                        Tracking = drainForWorld t traits :: acc.Tracking
                        IsInitialPopulation = acc.IsInitialPopulation || wasInitial
                }
            | Removed(traits, tracker) ->
                let t = tracker :?> TestTracker
                let wasInitial = not (t.HasBeenDrained world.WorldId)

                {
                    acc with
                        Tracking = drainForWorld t traits :: acc.Tracking
                        IsInitialPopulation = acc.IsInitialPopulation || wasInitial
                }
            | Changed(traits, tracker) ->
                let t = tracker :?> TestTracker
                let wasInitial = not (t.HasBeenDrained world.WorldId)

                {
                    acc with
                        Tracking = drainForWorld t traits :: acc.Tracking
                        IsInitialPopulation = acc.IsInitialPopulation || wasInitial
                }

        let matches = where |> Array.fold collect MatchedEntities.Empty

        // When tracking modifiers are present, they define the candidate set
        // (since tracked entities may no longer have traits — e.g. Removed/Changed+removed).
        // When no tracking is present, use the standard With/Or matching.
        let positiveMatches =
            match matches.Tracking with
            | [] ->
                match matches.With, matches.Or.IsEmpty with
                | [], true -> world |> allEntities // No positive criteria, so match all.
                | [], false -> matches.Or // No With criteria, so only Or matches count.
                | _, true -> Set.intersectMany matches.With // No Or criteria, so only With matches count.
                | _, false -> Set.intersect matches.Or (Set.intersectMany matches.With) // Apply both With and Or.
            | trackingSets ->
                let trackedEntities = Set.intersectMany trackingSets

                // Koota's initial population path for tracking queries only checks tracking
                // bitmasks, skipping With/Or filters. This is a known bug:
                // https://github.com/pmndrs/koota/issues/241
                // We intentionally replicate this behavior so mock-based unit tests run
                // consistently with the app running against real Koota.
                if matches.IsInitialPopulation then
                    trackedEntities
                else
                    match matches.With, matches.Or.IsEmpty with
                    | [], true -> trackedEntities
                    | [], false -> Set.intersect trackedEntities matches.Or
                    | _, true -> Set.intersect trackedEntities (Set.intersectMany matches.With)
                    | _, false ->
                        Set.intersect trackedEntities (Set.intersect matches.Or (Set.intersectMany matches.With))

        // Exclude the world entity from query results.
        let (EntityId worldEntityId) = world.EntityId

        let finalMatches =
            Set.difference positiveMatches (matches.Not |> Set.union (set [ worldEntityId ]))

        finalMatches |> Seq.map EntityId

    let queryFirst where world =
        let results = world |> query where
        if Seq.isEmpty results then None else Some(Seq.head results)

    let spawn traits world =
        let entity = world |> allocEntity

        let addTagTrait tag = world |> addTrait tag entity

        let addValueTrait (someTrait: ITrait, value) =
            let (EntityId entityId) = entity
            // Since we don't know the type of the value, we need to access the store directly.
            let store = world |> getStore someTrait
            let testTrait = someTrait :?> ITestUntypedValueTrait
            let mutableValue = testTrait.UnfreezeUntypedValue value

            if store.TryAdd(entityId, Some mutableValue) then
                TrackerRegistry.notifyAdded someTrait entity
                TrackerRegistry.cancelRemoved someTrait entity

        for someTrait in traits do
            someTrait |> TraitSpec.Map addTagTrait addValueTrait

        entity

type private Universe private () =
    let worldsLock = obj ()

    // The Universe tends to live for the lifetime of the process, so it needs to be both concurrency-safe
    // and not hang on to Worlds forever.
    let worlds: (WeakReference<World>)[] = Array.create maxWorlds null

    // ASSUMPTION: This is only called by CreateWorld, so worldsLock has already been acquired.
    let allocWorldId () =
        let isDeadWorld: WeakReference<World> -> bool =
            function
            | null -> true
            | weakRef ->
                let mutable world = Unchecked.defaultof<World>
                not (weakRef.TryGetTarget(&world))

        let collectDeadWorlds () =
            for i = 0 to worlds.Length - 1 do
                if isDeadWorld worlds[i] then
                    worlds[i] <- null

        let rec tryallocWorldId retryCount =
            if retryCount > 1 then
                failwith "TestWorld: too many worlds (max 16)"

            match worlds |> Array.tryFindIndex isDeadWorld with
            | Some nextId -> nextId
            | None ->
                collectDeadWorlds ()
                tryallocWorldId (retryCount + 1)

        tryallocWorldId 0

    let findWorld entity =
        let worldId = entity |> getWorldId

        let fail () =
            invalidArg (nameof worldId) $"No world registered for id {worldId}"

        // This functions is called from all over the place, so it has to do its own locking.
        lock worldsLock
        <| fun () ->
            // World IDs must be in-bounds by construction.
            assert (0 <= worldId && worldId < worlds.Length)

            match worlds[worldId] with
            | null -> fail ()
            | weakRef ->
                let mutable world = Unchecked.defaultof<World>

                if weakRef.TryGetTarget(&world) then world else fail ()

    // ASSUMPTION: This is only called by CreateWorld, so worldsLock has already been acquired.
    let registerWorld world =
        let weakRef = WeakReference<World> world
        worlds[world.WorldId] <- weakRef // Index has been allocated under lock, so this should always be in-bounds.
        world

    static member val Instance = Universe()

    member _.CreateWorld() =
        lock worldsLock <| fun () -> allocWorldId () |> createWorld |> registerWorld

    member _.UnregisterWorld worldId =
        // World IDs must be in-bounds by construction.
        assert (0 <= worldId && worldId < worlds.Length)
        lock worldsLock <| fun () -> worlds[worldId] <- null

    interface IEntityOperations with
        member _.Add someTrait entity =
            findWorld entity |> addTrait someTrait entity

        member _.Destroy entity = findWorld entity |> destroy entity

        member _.FriendlyId entity = getLocalId entity

        member _.Get valueTrait entity =
            findWorld entity |> getTraitValue valueTrait entity

        member _.Has someTrait entity =
            findWorld entity |> hasTrait someTrait entity

        member _.Remove someTrait entity =
            findWorld entity |> removeTrait someTrait entity

        member _.Set valueTrait value entity =
            findWorld entity |> setTraitValue valueTrait value entity

        member _.SetWith valueTrait update entity =
            findWorld entity |> setTraitValueWith valueTrait update entity

        member _.TargetFor relation entity =
            findWorld entity |> targetFor relation entity

        member _.TargetsFor relation entity =
            findWorld entity |> targetsFor relation entity

type TestWorld() =
    let world = Universe.Instance.CreateWorld()
    let worldEntity = world.EntityId

    interface IDisposable with
        member _.Dispose() =
            Universe.Instance.UnregisterWorld world.WorldId

    interface IWorld with
        member _.Add someTrait = world |> addTrait someTrait worldEntity

        member _.Get valueTrait =
            world |> getTraitValue valueTrait worldEntity

        member _.Has someTrait = world |> hasTrait someTrait worldEntity

        member _.Query where =
            let entities = world |> query where
            QueryResult.Create(entities, (fun _ -> ()), (fun _ -> ()), (fun _ _ _ _ -> ()), false, fun _ _ -> ())

        member _.QueryTrait(someTrait, where) =
            let entities = world |> query [| With someTrait; yield! where |]
            let testTrait = someTrait :?> ITestValueTrait<'T>

            let getMutable entity =
                world |> forceGetTraitValue someTrait entity :?> 'TMutable

            let getRead entity =
                match world |> getTraitValue someTrait entity with
                | Some v -> v
                | None ->
                    // Entity lost the trait between query time and read time.
                    // Return the schema default to match Koota's snapshot behavior.
                    let testUntypedTrait = someTrait :?> ITestUntypedValueTrait
                    testUntypedTrait.DefaultMutableValue.Value |> testTrait.FreezeValue

            let hasChanged =
                where
                |> Array.exists (function
                    | Changed(traits, _) -> traits |> Array.exists (fun t -> obj.ReferenceEquals(t, someTrait))
                    | _ -> false)

            let notifyChanges _ entity before after =
                if not (obj.Equals(before, after)) then
                    TrackerRegistry.notifyChanged someTrait entity

            let getReadResilient before entity =
                world |> getTraitValue someTrait entity |> Option.defaultValue before

            QueryResult.Create(entities, getRead, getMutable, notifyChanges, hasChanged, getReadResilient)

        member _.QueryTraits(firstTrait, secondTrait, where) =
            let entities = world |> query [| With firstTrait; With secondTrait; yield! where |]
            let firstTestTrait = firstTrait :?> ITestValueTrait<'T>
            let secondTestTrait = secondTrait :?> ITestValueTrait<'U>

            let getMutable entity =
                let firstValue, secondValue =
                    world |> forceGetTraitValue firstTrait entity, world |> forceGetTraitValue secondTrait entity

                firstValue :?> 'TMutable, secondValue :?> 'UMutable

            let getRead entity =
                let firstValue, secondValue = getMutable entity
                firstValue |> firstTestTrait.FreezeValue, secondValue |> secondTestTrait.FreezeValue

            let changedTraits =
                where
                |> Array.collect (function
                    | Changed(traits, _) -> traits
                    | _ -> Array.empty)

            let isTracked option someTrait =
                match option with
                | AlwaysTrack -> true
                | _ -> changedTraits |> Array.exists (fun t -> obj.ReferenceEquals(t, someTrait))

            let notifyChanges option entity (beforeFirst, beforeSecond) (afterFirst, afterSecond) =
                if isTracked option firstTrait && not (obj.Equals(beforeFirst, afterFirst)) then
                    TrackerRegistry.notifyChanged firstTrait entity

                if isTracked option secondTrait && not (obj.Equals(beforeSecond, afterSecond)) then
                    TrackerRegistry.notifyChanged secondTrait entity

            let hasChanged = changedTraits.Length > 0

            // Resilient after-read: if a queried trait was removed during UpdateEachWith,
            // fall back to the before-value for that trait so we don't crash. Traits that
            // are still present get read normally for proper change detection.
            let getReadResilient (beforeFirst, beforeSecond) entity =
                let afterFirst =
                    world |> getTraitValue firstTrait entity |> Option.defaultValue beforeFirst

                let afterSecond =
                    world |> getTraitValue secondTrait entity |> Option.defaultValue beforeSecond

                afterFirst, afterSecond

            QueryResult.Create(entities, getRead, getMutable, notifyChanges, hasChanged, getReadResilient)

        member _.QueryTraits3(firstTrait, secondTrait, thirdTrait, where) =
            let entities =
                world
                |> query [| With firstTrait; With secondTrait; With thirdTrait; yield! where |]

            let firstTestTrait = firstTrait :?> ITestValueTrait<'T>
            let secondTestTrait = secondTrait :?> ITestValueTrait<'U>
            let thirdTestTrait = thirdTrait :?> ITestValueTrait<'V>

            let getMutable entity =
                let firstValue, secondValue, thirdValue =
                    world |> forceGetTraitValue firstTrait entity,
                    world |> forceGetTraitValue secondTrait entity,
                    world |> forceGetTraitValue thirdTrait entity

                firstValue :?> 'TMutable, secondValue :?> 'UMutable, thirdValue :?> 'VMutable

            let getRead entity =
                let firstValue, secondValue, thirdValue = getMutable entity

                firstValue |> firstTestTrait.FreezeValue,
                secondValue |> secondTestTrait.FreezeValue,
                thirdValue |> thirdTestTrait.FreezeValue

            let changedTraits =
                where
                |> Array.collect (function
                    | Changed(traits, _) -> traits
                    | _ -> Array.empty)

            let isTracked option someTrait =
                match option with
                | AlwaysTrack -> true
                | _ -> changedTraits |> Array.exists (fun t -> obj.ReferenceEquals(t, someTrait))

            let notifyChanges option entity (b1, b2, b3) (a1, a2, a3) =
                if isTracked option firstTrait && not (obj.Equals(b1, a1)) then
                    TrackerRegistry.notifyChanged firstTrait entity

                if isTracked option secondTrait && not (obj.Equals(b2, a2)) then
                    TrackerRegistry.notifyChanged secondTrait entity

                if isTracked option thirdTrait && not (obj.Equals(b3, a3)) then
                    TrackerRegistry.notifyChanged thirdTrait entity

            let hasChanged = changedTraits.Length > 0

            let getReadResilient (b1, b2, b3) entity =
                let a1 = world |> getTraitValue firstTrait entity |> Option.defaultValue b1
                let a2 = world |> getTraitValue secondTrait entity |> Option.defaultValue b2
                let a3 = world |> getTraitValue thirdTrait entity |> Option.defaultValue b3
                a1, a2, a3

            QueryResult.Create(entities, getRead, getMutable, notifyChanges, hasChanged, getReadResilient)

        member _.QueryTraits4(firstTrait, secondTrait, thirdTrait, fourthTrait, where) =

            let entities =
                world
                |> query [|
                    With firstTrait
                    With secondTrait
                    With thirdTrait
                    With fourthTrait
                    yield! where
                |]

            let firstTestTrait = firstTrait :?> ITestValueTrait<'T>
            let secondTestTrait = secondTrait :?> ITestValueTrait<'U>
            let thirdTestTrait = thirdTrait :?> ITestValueTrait<'V>
            let fourthTestTrait = fourthTrait :?> ITestValueTrait<'W>

            let getMutable entity =
                let firstValue, secondValue, thirdValue, fourthValue =
                    world |> forceGetTraitValue firstTrait entity,
                    world |> forceGetTraitValue secondTrait entity,
                    world |> forceGetTraitValue thirdTrait entity,
                    world |> forceGetTraitValue fourthTrait entity

                firstValue :?> 'TMutable, secondValue :?> 'UMutable, thirdValue :?> 'VMutable, fourthValue :?> 'WMutable

            let getRead entity =
                let firstValue, secondValue, thirdValue, fourthValue = getMutable entity

                firstValue |> firstTestTrait.FreezeValue,
                secondValue |> secondTestTrait.FreezeValue,
                thirdValue |> thirdTestTrait.FreezeValue,
                fourthValue |> fourthTestTrait.FreezeValue

            let changedTraits =
                where
                |> Array.collect (function
                    | Changed(traits, _) -> traits
                    | _ -> Array.empty)

            let isTracked option someTrait =
                match option with
                | AlwaysTrack -> true
                | _ -> changedTraits |> Array.exists (fun t -> obj.ReferenceEquals(t, someTrait))

            let notifyChanges option entity (b1, b2, b3, b4) (a1, a2, a3, a4) =
                if isTracked option firstTrait && not (obj.Equals(b1, a1)) then
                    TrackerRegistry.notifyChanged firstTrait entity

                if isTracked option secondTrait && not (obj.Equals(b2, a2)) then
                    TrackerRegistry.notifyChanged secondTrait entity

                if isTracked option thirdTrait && not (obj.Equals(b3, a3)) then
                    TrackerRegistry.notifyChanged thirdTrait entity

                if isTracked option fourthTrait && not (obj.Equals(b4, a4)) then
                    TrackerRegistry.notifyChanged fourthTrait entity

            let hasChanged = changedTraits.Length > 0

            let getReadResilient (b1, b2, b3, b4) entity =
                let a1 = world |> getTraitValue firstTrait entity |> Option.defaultValue b1
                let a2 = world |> getTraitValue secondTrait entity |> Option.defaultValue b2
                let a3 = world |> getTraitValue thirdTrait entity |> Option.defaultValue b3
                let a4 = world |> getTraitValue fourthTrait entity |> Option.defaultValue b4
                a1, a2, a3, a4

            QueryResult.Create(entities, getRead, getMutable, notifyChanges, hasChanged, getReadResilient)

        member _.QueryFirst where = world |> queryFirst where

        member _.Remove someTrait =
            world |> removeTrait someTrait worldEntity

        member _.Set valueTrait value =
            world |> setTraitValue valueTrait value worldEntity

        member _.Spawn traits = world |> spawn traits

module TestECS =

    let maxWorlds = World.maxWorlds

    // This needs to be idempotent and thread-safe.
    let install () =
        match Globals.Instance.Entities with
        | :? Universe -> ()
        | _ -> Globals.Instance.Entities <- Universe.Instance

        match Globals.Instance.Traits with
        | :? TestTraitFactory -> ()
        | _ -> Globals.Instance.Traits <- TestTraitFactory()
