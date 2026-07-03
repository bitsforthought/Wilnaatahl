namespace Wilnaatahl.ECS.Mocks

open System
open System.Collections
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open System.Threading
open Wilnaatahl.ECS

/// The mock's untyped view of a relation. Relations key a per-(subject, target) value store; the
/// freeze/unfreeze conversions and the default mutable value let the store hold boxed values without
/// knowing their static type.
type ITestRelation =
    abstract member Config: RelationConfig
    /// Converts a boxed mutable value to its boxed read form.
    abstract member FreezeUntyped: value: obj -> obj
    /// Converts a boxed read value to its boxed mutable form.
    abstract member UnfreezeUntyped: value: obj -> obj
    /// The boxed mutable value stored when the relation is added (the schema default), if any.
    abstract member DefaultMutable: obj option

type private TestTrait(isTag) =
    interface ITrait with
        member _.IsTag = isTag

type private TestTagTrait() =
    inherit TestTrait(true)
    interface ITagTrait

type private ITestUntypedValueTrait =
    abstract UnfreezeUntypedValue: value: obj -> obj
    abstract DefaultMutableValue: obj option

type private ITestValueTrait<'T> =
    inherit IValueTrait<'T>
    inherit ITestUntypedValueTrait
    abstract FreezeValue: mutableValue: obj -> 'T
    abstract UnfreezeValue: value: 'T -> obj

type private TestValueTrait<'T, 'TMutable>(freeze: 'TMutable -> 'T, unfreeze: 'T -> 'TMutable, defaultValue: 'T option)
    =
    inherit TestTrait(false)
    interface IMutableValueTrait<'T, 'TMutable>

    interface ITestValueTrait<'T> with
        member _.UnfreezeUntypedValue value = unfreeze (value :?> 'T) :> obj
        member _.FreezeValue mutableValue = freeze (mutableValue :?> 'TMutable)
        member _.UnfreezeValue value = unfreeze value
        member _.DefaultMutableValue = defaultValue |> Option.map (fun v -> unfreeze v :> obj)

type private TestRelation<'T, 'TMutable>
    (config: RelationConfig, freeze: 'TMutable -> 'T, unfreeze: 'T -> 'TMutable, defaultValue: 'T option) =

    interface ITestRelation with
        member _.Config = config
        member _.FreezeUntyped value = freeze (value :?> 'TMutable) :> obj
        member _.UnfreezeUntyped value = unfreeze (value :?> 'T) :> obj
        member _.DefaultMutable = defaultValue |> Option.map (fun v -> unfreeze v :> obj)

    interface IRelation with
        member _.IsExclusive = config.IsExclusive

    interface IRelation<'T, 'TMutable>

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
            // ForEach reads each value with get (getRead), which is correct even for entities that
            // lost the trait between query and read time (getRead falls back to the schema default).
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
    // For each flagged (trait, entity) we also record a snapshot of the entity's other traits at
    // the instant the tracked event fired. Koota's event-driven tracking queries require their
    // With/Or/Not filters to have held at that moment (they are then re-checked against the current
    // state at drain time), so the snapshot is what the event-time half of that check runs against.
    // The snapshot is a small list of trait references.
    let flags = ConcurrentDictionary<ITrait, ConcurrentDictionary<int, ITrait list>>()

    let drainedWorlds = ConcurrentDictionary<int, bool>()

    member _.Flag(someTrait: ITrait, EntityId entityId, snapshot: ITrait list) =
        let traitFlags =
            flags.GetOrAdd(someTrait, fun _ -> ConcurrentDictionary<int, ITrait list>())

        traitFlags[entityId] <- snapshot

    member _.Unflag(someTrait: ITrait, EntityId entityId) =
        match flags.TryGetValue someTrait with
        | true, traitFlags -> traitFlags.TryRemove entityId |> ignore
        | false, _ -> ()

    /// Returns true if this tracker has been drained for the given world before.
    /// Koota skips With/Or filters on the first drain (initial population) but applies
    /// them on subsequent drains (event-driven path).
    member _.HasBeenDrained(worldId: int) = drainedWorlds.ContainsKey worldId

    /// Drains the given trait for the given world, returning each flagged entity together with
    /// the snapshot of its traits at the time the tracked event fired.
    member _.DrainTrait(someTrait: ITrait, worldId: int) : Map<int, ITrait list> =
        drainedWorlds[worldId] <- true

        match flags.TryGetValue someTrait with
        | true, traitFlags ->
            let matching =
                traitFlags
                |> Seq.filter (fun kvp -> (EntityId kvp.Key) |> getWorldId = worldId)
                |> Seq.map (fun kvp -> kvp.Key, kvp.Value)
                |> Map.ofSeq

            for eid in matching |> Map.toSeq |> Seq.map fst do
                traitFlags.TryRemove eid |> ignore

            matching
        | false, _ -> Map.empty

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

    // The snapshot is passed lazily: computing it scans every trait store. It is forced only by
    // the loop body below, which doesn't run when no tracker of the relevant kind is registered,
    // so the scan is skipped entirely in that (common) case.
    let notifyAdded someTrait entity (snapshot: Lazy<ITrait list>) =
        for kvp in addedTrackers do
            kvp.Key.Flag(someTrait, entity, snapshot.Value)

    let notifyRemoved someTrait entity (snapshot: Lazy<ITrait list>) =
        for kvp in removedTrackers do
            kvp.Key.Flag(someTrait, entity, snapshot.Value)

    let cancelRemoved someTrait entity =
        for kvp in removedTrackers do
            kvp.Key.Unflag(someTrait, entity)

    let cancelAdded someTrait entity =
        for kvp in addedTrackers do
            kvp.Key.Unflag(someTrait, entity)

    let notifyChanged someTrait entity (snapshot: Lazy<ITrait list>) =
        for kvp in changedTrackers do
            kvp.Key.Flag(someTrait, entity, snapshot.Value)

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

        member _.Relation config =
            TestRelation<unit, unit>(config, id, id, Some())

        member _.RelationWith(config, store, mutableStore) =
            let freezeUntyped, unfreezeUntyped = findConversionMethods store mutableStore
            let freeze (m: 'TMutable) = freezeUntyped m :?> 'T
            let unfreeze (v: 'T) = unfreezeUntyped v :?> 'TMutable
            TestRelation<'T, 'TMutable>(config, freeze, unfreeze, Some store)

        member _.TagTrait() = TestTagTrait()

        member _.TraitWith value mutableValue =
            let freezeUntyped, unfreezeUntyped = findConversionMethods value mutableValue
            let freeze (m: 'TMutable) = freezeUntyped m :?> 'T
            let unfreeze (v: 'T) = unfreezeUntyped v :?> 'TMutable
            TestValueTrait<'T, 'TMutable>(freeze, unfreeze, Some value)

        member _.TraitWithRef _ = TestValueTrait<'T, 'T>(id, id, None)

[<AutoOpen>]
module private World =
    type World = {
        WorldId: int
        EntityId: EntityId
        TraitStores: ConcurrentDictionary<ITrait, ConcurrentDictionary<int, obj option>>
        /// Per-relation value store, keyed by the relation instance (by reference). The inner map is
        /// keyed by struct(subjectId, targetId) and holds the boxed mutable value (or None for none
        /// stored yet).
        RelationStores: ConcurrentDictionary<IRelation, ConcurrentDictionary<struct (int * int), obj option>>
        /// The set of live (spawned, not-yet-destroyed) local entity ids. Koota tracks entity liveness
        /// independent of traits, so this — not the trait stores — defines the world's membership: a
        /// bare Spawn [||] or a relation-only entity is a member and appears in an unfiltered Query().
        ActiveEntities: ConcurrentDictionary<int, unit>
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
        // Membership is the set of live entities, not the union of trait-store keys: bare and
        // relation-only entities have no trait rows but are still returned by an unfiltered query.
        world.ActiveEntities.Keys |> Set.ofSeq

    /// Snapshot of the traits the given entity currently has. Captured when a tracked event fires
    /// so that event-driven tracking queries can evaluate their With/Or/Not filters as of that
    /// moment (the result is then also re-checked against the current state), matching Koota.
    let entityTraitSnapshot (EntityId entityId) world =
        world.TraitStores
        |> Seq.choose (fun kvp ->
            if kvp.Value.ContainsKey entityId then
                Some kvp.Key
            else
                None)
        |> List.ofSeq

    let private allocEntity world =
        let entityId = Interlocked.Increment(&world.NextEntityId)
        let entity = packEntityId world.WorldId entityId
        // Register the entity as live the moment it is allocated, so even a bare Spawn [||] (no
        // traits, no relations) is a member of the world until it is destroyed. The key is the full
        // packed id, matching how trait/relation stores and query sets key their entities.
        let (EntityId packedId) = entity
        world.ActiveEntities.TryAdd(packedId, ()) |> ignore
        entity

    let createWorld id = {
        WorldId = id
        EntityId =
            // World-level special entity has ID 0.
            let worldEntityLocalId = 0
            packEntityId id worldEntityLocalId

        TraitStores = ConcurrentDictionary<ITrait, ConcurrentDictionary<int, obj option>>()
        RelationStores = ConcurrentDictionary<IRelation, ConcurrentDictionary<struct (int * int), obj option>>()
        ActiveEntities = ConcurrentDictionary<int, unit>()
        NextEntityId = 0 // First entity will get ID 1 since it's assigned after Interlocked.Increment.
    }

    let hasTrait someTrait (EntityId entityId) world =
        let store = world |> getStore someTrait
        store.ContainsKey entityId

    /// Returns the per-(subject, target) value store for the given relation instance, creating it if needed.
    let private getRelationStore (relation: #IRelation) world =
        world.RelationStores.GetOrAdd(
            (relation :> IRelation),
            fun _ -> ConcurrentDictionary<struct (int * int), obj option>()
        )

    /// Returns the subject ids related via the given relation. When a target id is given, only subjects
    /// related to that specific target are returned; otherwise all subjects (the wildcard case).
    let relatedSubjects (relation: #IRelation) (maybeTargetId: int option) world =
        let store = world |> getRelationStore relation

        store.Keys
        |> Seq.choose (fun (struct (subjectId, targetId)) ->
            match maybeTargetId with
            | Some target when target <> targetId -> None
            | _ -> Some subjectId)
        |> Set.ofSeq

    let removeTrait someTrait (EntityId entityId) world =
        let store = world |> getStore someTrait
        let existed, _ = store.TryRemove(entityId)

        if existed then
            let snapshot = lazy (world |> entityTraitSnapshot (EntityId entityId))
            TrackerRegistry.notifyRemoved someTrait (EntityId entityId) snapshot
            TrackerRegistry.cancelAdded someTrait (EntityId entityId)

    let addTrait (someTrait: ITrait) entity world =
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

            TrackerRegistry.notifyAdded someTrait entity (lazy (world |> entityTraitSnapshot entity))
            TrackerRegistry.cancelRemoved someTrait entity

    let addRelation (relation: IRelation<'T, 'TMutable>) target (EntityId subjectId) world =
        let testRelation = relation :?> ITestRelation
        let store = world |> getRelationStore relation
        let (EntityId targetId) = target

        // For an exclusive relation a subject can have only one target, so drop any existing target
        // that DIFFERS from the new one. Re-adding the SAME target must be a no-op that preserves the
        // existing value (matching Koota), so it is left untouched here and by the TryAdd below.
        if testRelation.Config.IsExclusive then
            store.Keys
            |> Seq.filter (fun (struct (existingSubject, existingTarget)) ->
                existingSubject = subjectId && existingTarget <> targetId)
            |> Seq.iter (fun key -> store.TryRemove key |> ignore)

        // Relations initialize with their schema default mutable value (matching Koota's behavior).
        // TryAdd is a no-op when the (subject, target) pair already exists, preserving its value.
        store.TryAdd(struct (subjectId, targetId), testRelation.DefaultMutable)
        |> ignore

    let removeRelation (relation: IRelation<'T, 'TMutable>) target (EntityId subjectId) world =
        let store = world |> getRelationStore relation
        let (EntityId targetId) = target
        store.TryRemove(struct (subjectId, targetId)) |> ignore

    let hasRelation (relation: IRelation<'T, 'TMutable>) target (EntityId subjectId) world =
        let store = world |> getRelationStore relation
        let (EntityId targetId) = target
        store.ContainsKey(struct (subjectId, targetId))

    let getRelationValue (relation: IRelation<'T, 'TMutable>) target (EntityId subjectId) world : 'T option =
        let testRelation = relation :?> ITestRelation
        let store = world |> getRelationStore relation
        let (EntityId targetId) = target

        match store.TryGetValue(struct (subjectId, targetId)) with
        | true, Some boxedMutable -> Some(testRelation.FreezeUntyped boxedMutable :?> 'T)
        | _ -> None

    let setRelationValue (relation: IRelation<'T, 'TMutable>) target (value: 'T) (EntityId subjectId) world =
        let testRelation = relation :?> ITestRelation
        let store = world |> getRelationStore relation
        let (EntityId targetId) = target
        let key = struct (subjectId, targetId)

        match store.TryGetValue key with
        | true, _ -> store[key] <- Some(testRelation.UnfreezeUntyped(value :> obj))
        | false, _ ->
            // Setting a value for an absent relation throws. The message prefix (up to the entity
            // id) is the cross-backend contract, mirrored verbatim in src/ecs/koota/kootaWrapper.ts;
            // the trailing entity id is non-deterministic across backends and not part of it.
            failwith $"Cannot set a value for a relation that is not present on the subject entity {subjectId}"

    let destroy entity world =
        for someTrait in world.TraitStores.Keys do
            world |> removeTrait someTrait entity

        // Koota auto-cleans relations when either the subject or the target is destroyed.
        let (EntityId entityId) = entity

        for relationStore in world.RelationStores.Values do
            relationStore.Keys
            |> Seq.filter (fun (struct (subjectId, targetId)) -> subjectId = entityId || targetId = entityId)
            |> Seq.iter (fun key -> relationStore.TryRemove key |> ignore)

        // The entity is no longer live: drop it from the world's membership so it stops appearing in
        // unfiltered and Not-only queries.
        world.ActiveEntities.TryRemove entityId |> ignore

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
                let snapshot = lazy (world |> entityTraitSnapshot (EntityId entityId))
                TrackerRegistry.notifyChanged valueTrait (EntityId entityId) snapshot
        | false, _ -> invalidArg (nameof valueTrait) $"Trait not present on entity {entityId}"

    let setTraitValueWith (valueTrait: IValueTrait<'T>) (update: 'T -> 'T) (EntityId entityId) world =
        let store = world |> getStore valueTrait

        match store.TryGetValue entityId with
        | true, (Some v as value) ->
            let testTrait = valueTrait :?> ITestValueTrait<'T>
            let newValue = update (v |> testTrait.FreezeValue)

            if store.TryUpdate(entityId, Some(newValue |> testTrait.UnfreezeValue), value) then
                let snapshot = lazy (world |> entityTraitSnapshot (EntityId entityId))
                TrackerRegistry.notifyChanged valueTrait (EntityId entityId) snapshot
        | _ -> invalidArg (nameof valueTrait) $"Trait value not set on entity {entityId}"

    let targetsFor (relation: IRelation<'T, 'TMutable>) (EntityId subjectId) world =
        let store = world |> getRelationStore relation

        store.Keys
        |> Seq.choose (fun (struct (existingSubject, targetId)) ->
            if existingSubject = subjectId then
                Some(EntityId targetId)
            else
                None)
        |> Array.ofSeq

    let targetFor relation entity world =
        let targets = world |> targetsFor relation entity

        if targets |> Array.isEmpty then None else Some targets[0]

    type private MatchedEntities = {
        WithTraits: ITrait list
        OrTraits: ITrait list
        NotTraits: ITrait list
        /// Each entry is the set of subject ids matched by a single Related/RelatedToAny operator,
        /// evaluated against the CURRENT world state at drain time (never snapshotted at a tracked
        /// event's moment). This is deliberate: relations are not traits, so they never appear in the
        /// event-time trait snapshots, and — unlike With/Or/Not — a relation filter is only ever
        /// re-checked against the present. Combining a change tracker with a relation filter whose
        /// relation is mutated between the tracked event and the drain is intentionally NOT supported,
        /// even though Koota permits the combination: Koota's exact event-time relation semantics are
        /// unverified, so we do not add speculative relation snapshotting. Multiple entries are ANDed
        /// together.
        RelatedSets: Set<int> list
        /// Each entry maps the entities matched by a single tracking modifier (ANDed across its
        /// tracked traits) to the snapshot of their traits at the time the tracked event fired.
        /// Multiple tracking modifiers are ANDed together.
        Tracking: Map<int, ITrait list> list
        /// True if any tracker in the query is being drained for the first time (initial population).
        /// Koota skips With/Or filters during initial population.
        IsInitialPopulation: bool
    } with

        static member val Empty =
            {
                WithTraits = []
                OrTraits = []
                NotTraits = []
                RelatedSets = []
                Tracking = []
                IsInitialPopulation = false
            }

    let query where world =
        let getEntitySet (someTrait: ITrait) =
            let store = world |> getStore someTrait
            store.Keys |> Set.ofSeq

        let getEntitySetUnion (traits: seq<ITrait>) =
            traits |> Seq.map getEntitySet |> Set.unionMany

        // True if a snapshot (the traits an entity held when a tracked event fired) satisfies the
        // given filter trait. Relations aren't traits and never appear in snapshots, so relation
        // filters are handled separately via RelatedSets (evaluated at drain time).
        let snapshotHasTrait (snapshot: ITrait list) (filterTrait: ITrait) = snapshot |> List.contains filterTrait

        // ANDs a set of per-trait/per-modifier drain results by keeping only entities present in
        // every map, and merges (unions) their at-event-time snapshots.
        let intersectSnapshots (maps: Map<int, ITrait list> seq) : Map<int, ITrait list> =
            let maps = maps |> Seq.toList

            match maps with
            | [] -> Map.empty
            | _ ->
                let commonKeys = maps |> List.map (Map.keys >> Set.ofSeq) |> Set.intersectMany

                commonKeys
                |> Set.toSeq
                |> Seq.map (fun eid -> eid, maps |> List.choose (Map.tryFind eid) |> List.concat |> List.distinct)
                |> Map.ofSeq

        let drainForWorld (testTracker: TestTracker) (traits: ITrait[]) =
            // Drain every tracked trait (side-effecting) before ANDing their results.
            traits
            |> Array.map (fun t -> testTracker.DrainTrait(t, world.WorldId))
            |> intersectSnapshots

        // Drains a tracking modifier (ANDing its tracked traits) and records whether this is the
        // tracker's first drain for this world (initial population, which skips With/Or filters).
        let withTracking acc (traits: ITrait[]) (tracker: ITracker) =
            let t = tracker :?> TestTracker
            let wasInitial = not (t.HasBeenDrained world.WorldId)

            {
                acc with
                    Tracking = drainForWorld t traits :: acc.Tracking
                    IsInitialPopulation = acc.IsInitialPopulation || wasInitial
            }

        let collect acc queryOp =
            match queryOp with
            | With someTrait -> { acc with WithTraits = someTrait :: acc.WithTraits }
            | Or traits -> { acc with OrTraits = List.ofArray traits @ acc.OrTraits }
            | Not traits -> { acc with NotTraits = List.ofArray traits @ acc.NotTraits }
            | Added(traits, tracker) -> withTracking acc traits tracker
            | Removed(traits, tracker) -> withTracking acc traits tracker
            | Changed(traits, tracker) -> withTracking acc traits tracker
            | Related(relation, EntityId targetId) -> {
                acc with
                    RelatedSets = (world |> relatedSubjects relation (Some targetId)) :: acc.RelatedSets
              }
            | RelatedToAny relation -> {
                acc with
                    RelatedSets = (world |> relatedSubjects relation None) :: acc.RelatedSets
              }

        let matches = where |> Array.fold collect MatchedEntities.Empty

        let withSets = matches.WithTraits |> List.map getEntitySet
        let orSet = matches.OrTraits |> getEntitySetUnion
        let notSet = matches.NotTraits |> getEntitySetUnion

        // Relation filters (Related/RelatedToAny) are drain-time positive sets, ANDed with With.
        let positiveSets = withSets @ matches.RelatedSets

        // When tracking modifiers are present, they define the candidate set
        // (since tracked entities may no longer have traits — e.g. Removed/Changed+removed).
        // When no tracking is present, use the standard With/Or/Related matching.
        let positiveMatches =
            match matches.Tracking with
            | [] ->
                match positiveSets, matches.OrTraits.IsEmpty with
                | [], true -> world |> allEntities // No positive criteria, so match all.
                | [], false -> orSet // No With/Related criteria, so only Or matches count.
                | _, true -> Set.intersectMany positiveSets // No Or criteria, so only With/Related matches count.
                | _, false -> Set.intersect orSet (Set.intersectMany positiveSets) // Apply both.
            | trackingMaps ->
                let tracked = intersectSnapshots trackingMaps
                let trackedEntities = tracked |> Map.keys |> Set.ofSeq

                // Koota's initial population path for tracking queries only checks tracking
                // bitmasks for the *trait* filters, skipping With/Or. This is a known bug:
                // https://github.com/pmndrs/koota/issues/241
                // We intentionally replicate this behavior so mock-based unit tests run
                // consistently with the app running against real Koota.
                // Relation filters (Related/RelatedToAny) are NOT part of that skip: real Koota
                // still applies them during initial population (confirmed against real Koota via
                // the portable conformance tests), so we intersect the tracked entities with RelatedSets.
                if matches.IsInitialPopulation then
                    trackedEntities
                    |> Set.filter (fun eid -> matches.RelatedSets |> List.forall (Set.contains eid))
                else
                    // Event-driven path: Koota includes an entity only if the query's With/Or/Not
                    // filters held at the moment the tracked event fired (checked against the
                    // entity's snapshot) AND still hold against the current state at drain time
                    // (Koota evicts an entity when a later structural change — including destroy —
                    // breaks a filter). We therefore require both. Drain-time Not is applied by the
                    // final Set.difference below; the per-entity checks here add the event-time
                    // With/Or/Not and the drain-time With/Or.
                    trackedEntities
                    |> Set.filter (fun eid ->
                        let snapshot = tracked[eid]
                        let eventWithOk = matches.WithTraits |> List.forall (snapshotHasTrait snapshot)

                        let eventOrOk =
                            matches.OrTraits.IsEmpty
                            || matches.OrTraits |> List.exists (snapshotHasTrait snapshot)

                        let eventNotOk =
                            matches.NotTraits |> List.forall (fun t -> not (snapshotHasTrait snapshot t))

                        let drainWithOk = withSets |> List.forall (Set.contains eid)
                        let drainOrOk = matches.OrTraits.IsEmpty || Set.contains eid orSet
                        // Relation filters have no event-time snapshot (see RelatedSets): they are
                        // only ever evaluated against the current world state at drain time.
                        let drainRelatedOk = matches.RelatedSets |> List.forall (Set.contains eid)

                        eventWithOk
                        && eventOrOk
                        && eventNotOk
                        && drainWithOk
                        && drainOrOk
                        && drainRelatedOk)

        // Exclude the world entity from query results.
        let (EntityId worldEntityId) = world.EntityId

        let finalMatches =
            Set.difference positiveMatches (notSet |> Set.union (set [ worldEntityId ]))

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
                TrackerRegistry.notifyAdded someTrait entity (lazy (world |> entityTraitSnapshot entity))
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

        member _.AddRelation relation target entity =
            findWorld entity |> addRelation relation target entity

        member _.RemoveRelation relation target entity =
            findWorld entity |> removeRelation relation target entity

        member _.HasRelation relation target entity =
            findWorld entity |> hasRelation relation target entity

        member _.GetRelationValue relation target entity =
            findWorld entity |> getRelationValue relation target entity

        member _.SetRelationValue relation target value entity =
            findWorld entity |> setRelationValue relation target value entity

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

            QueryResult.Create(entities, (fun _ -> ()), (fun _ -> ()), (fun _ _ _ _ -> ()), false, (fun _ _ -> ()))

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
                    TrackerRegistry.notifyChanged someTrait entity (lazy (world |> entityTraitSnapshot entity))

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
                let snapshot = lazy (world |> entityTraitSnapshot entity)

                if isTracked option firstTrait && not (obj.Equals(beforeFirst, afterFirst)) then
                    TrackerRegistry.notifyChanged firstTrait entity snapshot

                if isTracked option secondTrait && not (obj.Equals(beforeSecond, afterSecond)) then
                    TrackerRegistry.notifyChanged secondTrait entity snapshot

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
                let snapshot = lazy (world |> entityTraitSnapshot entity)

                if isTracked option firstTrait && not (obj.Equals(b1, a1)) then
                    TrackerRegistry.notifyChanged firstTrait entity snapshot

                if isTracked option secondTrait && not (obj.Equals(b2, a2)) then
                    TrackerRegistry.notifyChanged secondTrait entity snapshot

                if isTracked option thirdTrait && not (obj.Equals(b3, a3)) then
                    TrackerRegistry.notifyChanged thirdTrait entity snapshot

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
                let snapshot = lazy (world |> entityTraitSnapshot entity)

                if isTracked option firstTrait && not (obj.Equals(b1, a1)) then
                    TrackerRegistry.notifyChanged firstTrait entity snapshot

                if isTracked option secondTrait && not (obj.Equals(b2, a2)) then
                    TrackerRegistry.notifyChanged secondTrait entity snapshot

                if isTracked option thirdTrait && not (obj.Equals(b3, a3)) then
                    TrackerRegistry.notifyChanged thirdTrait entity snapshot

                if isTracked option fourthTrait && not (obj.Equals(b4, a4)) then
                    TrackerRegistry.notifyChanged fourthTrait entity snapshot

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
