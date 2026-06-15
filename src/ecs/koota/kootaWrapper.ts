import { int32 } from "../../generated/fable_modules/fable-library-ts.5.1.0/Int32.js";
import { Option } from "../../generated/fable_modules/fable-library-ts.5.1.0/Option";
import {
  ConfigurableTrait,
  createAdded,
  createChanged,
  createRemoved,
  Entity,
  InstancesFromParameters,
  Modifier,
  Not,
  Or,
  QueryParameter,
  QueryResult,
  QueryResultOptions,
  relation,
  Relation,
  Schema,
  SetTraitCallback,
  TagTrait,
  trait,
  Trait,
  TraitValue,
  World,
} from "koota";
import {
  ChangeDetectionOption,
  EntityId,
  IAddedTracker,
  IChangedTracker,
  IEntityOperations,
  IQueryResult$2 as IQueryResult,
  IRelation$1 as IRelation,
  IRemovedTracker,
  ITagTrait,
  ITrait,
  ITraitFactory,
  IValueTrait$1 as IValueTrait,
  IMutableValueTrait$2 as IMutableValueTrait,
  IWorld,
  QueryOperator,
  RelationConfig,
  TrackerType,
  TraitSpec_$union as TraitSpec,
  TraitSpec_Map,
  ITracker,
} from "../../generated/ECS/Types";

type KootaSchema<T> = T extends Schema ? T : never;
type KootaValueFactory<T> = () => T extends number
  ? number
  : T extends boolean
    ? boolean
    : T extends string
      ? string
      : T;
type KootaValueTrait<T> = Trait<KootaSchema<T>>;
type KootaValueFactoryTrait<T> = Trait<KootaValueFactory<T>>;
type KootaQueryParameters<S> = S extends QueryParameter[] ? S : [];
type KootaTracker<TType extends string = string> = <T extends Trait[] = Trait[]>(
  ...traits: T
) => Modifier;

type WrappedTracker = { Tracker: TrackerType; kootaTracker: KootaTracker };

type WrappedTrait<TKootaTrait extends Trait<any>> = { IsTag: boolean; trait: TKootaTrait };
type WrappedTagTrait = WrappedTrait<TagTrait>;
type WrappedValueTrait<T> = WrappedTrait<KootaValueTrait<T>>;
type WrappedValueFactoryTrait<T> = WrappedTrait<KootaValueFactoryTrait<T>>;

// A wrapped relation pair additionally carries the Koota relation function and its target so that
// Spawn can rebuild the pair in "params" form (rel(target, value)) when an initial value is given.
// In 0.6.x the pair object is opaque and a [pair, value] tuple is not a valid spawn argument, so
// the value must be supplied as relation params instead.
type WrappedRelationPair = WrappedTrait<Trait<any>> & {
  relationFn: Relation<Trait<any>>;
  relationTarget: Entity;
  isExclusive: boolean;
};

function isWrappedRelationPair(wrapper: unknown): wrapper is WrappedRelationPair {
  return typeof wrapper === "object" && wrapper !== null && "relationFn" in wrapper;
}

function isNonExclusiveRelationPair(wrapper: unknown): boolean {
  return isWrappedRelationPair(wrapper) && !wrapper.isExclusive;
}

// A relation pair used as a query's value slot cannot have its per-target value surfaced through
// the query's iteration state, so UpdateEach (which iterates that state) fails fast. ForEach is
// unaffected: it reads each entity's value individually, which is always correct.
const relationValueUpdateEachError =
  "Cannot UpdateEach a query whose value is a relation pair. Read the relation's value with ForEach instead; UpdateEach cannot read a relation pair's per-target value.";

type WrappedRelation<TKootaTrait extends Trait<any>, TTrait extends ITrait> = IRelation<TTrait> & {
  IsTag: boolean;
  rel: Relation<TKootaTrait>;
};

type WrappedTagRelation = WrappedRelation<TagTrait, ITagTrait>;

// The $wrapper symbol allows us to cache wrappers directly on Koota objects without conflicting
// with present or future Koota properties. While it's sketchy to mutate objects coming from Koota,
// we do it so they aren't allocated repeatedly, which will be important during rendering.
const $wrapper = Symbol.for("wilnaatahl.kootaWrapper");

type WithWrapper<T, W> = T & { [$wrapper]: W };

// Returns the hidden $wrapper property of the given object, creating and attaching it to the object
// with the given factory function if it doesn't already exist. The factory function will be passed
// the given object, but if it closes over it already, it's safe to ignore the parameter.
function getOrCreateWrapper<T extends object, TWrapper>(
  obj: T,
  createWrapper: (o: T) => TWrapper
): TWrapper {
  const objWithWrapper =
    $wrapper in obj
      ? (obj as WithWrapper<T, TWrapper>)
      : Object.assign(obj, {
          [$wrapper]: createWrapper(obj),
        });

  return objWithWrapper[$wrapper];
}

function validateWrappedTrait<TKootaTrait extends Trait<any>, T extends WrappedTrait<TKootaTrait>>(
  trait: ITrait,
  method: string
): T {
  if (!("trait" in trait)) {
    throw new Error(`Invalid ITrait implementation passed to ${method}().`);
  }

  return trait as T;
}

function toKootaTrait<T>(trait: ITrait): Trait<any> {
  const method = "toKootaTrait";
  const traitWrapper = trait.IsTag
    ? validateWrappedTrait<TagTrait, WrappedTagTrait>(trait, method)
    : validateWrappedTrait<KootaValueTrait<T>, WrappedValueTrait<T>>(trait, method);
  return traitWrapper.trait;
}

function toKootaValueTraitForRead<T>(trait: IValueTrait<T>): KootaValueTrait<T> {
  return validateWrappedTrait<KootaValueTrait<T>, WrappedValueTrait<T>>(
    trait,
    "toKootaValueTraitForRead"
  ).trait;
}

// We only export the strongly-typed versions, otherwise type inference fails to find the most
// specific trait type and things break.
export function toKootaValueTrait<T, TMutable>(
  trait: IMutableValueTrait<T, TMutable>
): KootaValueTrait<T> {
  return validateWrappedTrait<KootaValueTrait<T>, WrappedValueTrait<T>>(trait, "toKootaValueTrait")
    .trait;
}

export function toKootaValueFactoryTrait<T, TMutable>(
  trait: IMutableValueTrait<T, TMutable>
): KootaValueFactoryTrait<T> {
  return validateWrappedTrait<KootaValueFactoryTrait<T>, WrappedValueFactoryTrait<T>>(
    trait,
    "toKootaValueFactoryTrait"
  ).trait;
}

export function toKootaTagTrait(trait: ITagTrait): TagTrait {
  return validateWrappedTrait<TagTrait, WrappedTagTrait>(trait, "toKootaTagTrait").trait;
}

export function toKootaRelation<TTrait extends ITrait, T = void>(
  r: IRelation<TTrait>
): Relation<Trait<any>> {
  if (!("rel" in r)) {
    throw new Error("Invalid IRelation implementation passed to toKootaRelation().");
  }
  const wrapper = r.IsTag
    ? (r as WrappedTagRelation)
    : (r as WrappedRelation<KootaValueTrait<T>, TTrait>);
  return wrapper.rel;
}

export function createEntityOperations(): IEntityOperations {
  function Add(someTrait: ITrait, entity: EntityId & Entity): void {
    entity.add(toKootaTrait(someTrait));
  }

  function Destroy(entity: EntityId & Entity): void {
    entity.destroy();
  }

  function FriendlyId(entity: EntityId & Entity): int32 {
    return entity.id();
  }

  function Get<T>(valueTrait: IValueTrait<T>, entity: EntityId & Entity): Option<T> {
    return entity.get(toKootaValueTraitForRead(valueTrait));
  }

  function Has(someTrait: ITrait, entity: EntityId & Entity): boolean {
    return entity.has(toKootaTrait(someTrait));
  }

  function Remove(someTrait: ITrait, entity: EntityId & Entity): void {
    entity.remove(toKootaTrait(someTrait));
  }

  function Set<T>(valueTrait: IValueTrait<T>, value: T, entity: EntityId & Entity): void {
    const valueToSet = value as TraitValue<KootaSchema<T>>;
    entity.set(toKootaValueTraitForRead(valueTrait), valueToSet);
  }

  function SetWith<T>(
    valueTrait: IValueTrait<T>,
    update: (value: T) => T,
    entity: EntityId & Entity
  ): void {
    entity.set(
      toKootaValueTraitForRead(valueTrait),
      update as SetTraitCallback<KootaValueTrait<T>>
    );
  }

  function TargetFor<TTrait extends ITrait>(
    relation: IRelation<TTrait>,
    entity: EntityId & Entity
  ): Option<EntityId> {
    return entity.targetFor(toKootaRelation(relation));
  }

  function TargetsFor<TTrait extends ITrait>(
    relation: IRelation<TTrait>,
    entity: EntityId & Entity
  ): EntityId[] {
    return entity.targetsFor(toKootaRelation(relation));
  }

  return {
    Add,
    Destroy,
    FriendlyId,
    Get,
    Has,
    Remove,
    Set,
    SetWith,
    TargetFor,
    TargetsFor,
  };
}

export function createTraitFactory(): ITraitFactory {
  function fromKootaAddedTracker(tracker: KootaTracker): IAddedTracker {
    return getOrCreateWrapper(tracker, (t) => ({
      Tracker: { type: "addedTracker" },
      kootaTracker: t,
    }));
  }

  function fromKootaChangedTracker(tracker: KootaTracker): IChangedTracker {
    return getOrCreateWrapper(tracker, (t) => ({
      Tracker: { type: "changedTracker" },
      kootaTracker: t,
    }));
  }

  function fromKootaRemovedTracker(tracker: KootaTracker): IRemovedTracker {
    return getOrCreateWrapper(tracker, (t) => ({
      Tracker: { type: "removedTracker" },
      kootaTracker: t,
    }));
  }

  function fromKootaTrait<TKootaTrait extends Trait<any>>(
    trait: TKootaTrait,
    isTag: boolean
  ): ITrait {
    return getOrCreateWrapper(trait, (t) => ({ IsTag: isTag, trait: t }));
  }

  function fromKootaTagTrait(trait: TagTrait): ITagTrait {
    return fromKootaTrait(trait, true);
  }

  function fromKootaValueTrait<T, TMutable>(
    trait: KootaValueTrait<T>
  ): IMutableValueTrait<T, TMutable> {
    return fromKootaTrait(trait, false);
  }

  function fromKootaValueFactoryTrait<T>(
    trait: KootaValueFactoryTrait<T>
  ): IMutableValueTrait<T, T> {
    return fromKootaTrait(trait, false);
  }

  function fromKootaRelation<TKootaTrait extends Trait<any>, TTrait extends ITrait>(
    rel: Relation<TKootaTrait>,
    isTag: boolean,
    isExclusive: boolean
  ): IRelation<TTrait> {
    // A relation pair (rel(target)) is freshly allocated by Koota on every call, so — unlike
    // the stable trait objects fromKootaTrait caches a wrapper on — we must NOT cache here:
    // the cache would never hit. We also bypass fromKootaTrait's Trait<any> constraint because
    // a pair is a RelationPair, not a Trait; Koota accepts a pair anywhere a Trait is valid for
    // add/remove/has/get/set and queries, so we expose it through the wrapper's trait slot.
    function WithTarget(entity: EntityId & Entity): TTrait {
      const wrapped: WrappedRelationPair = {
        IsTag: isTag,
        trait: rel(entity) as unknown as Trait<any>,
        relationFn: rel as Relation<Trait<any>>,
        relationTarget: entity,
        isExclusive,
      };
      return wrapped as unknown as TTrait;
    }

    function Wildcard(): TTrait {
      const wrapped: WrappedTrait<Trait<any>> = {
        IsTag: true,
        trait: rel("*") as unknown as Trait<any>,
      };
      return wrapped as unknown as TTrait;
    }

    return getOrCreateWrapper(rel, (r) => ({ IsTag: isTag, rel: r, WithTarget, Wildcard }));
  }

  function fromKootaTagRelation(
    rel: Relation<Trait<any>>,
    isExclusive: boolean
  ): IRelation<ITagTrait> {
    return fromKootaRelation(rel, true, isExclusive);
  }

  function fromKootaValueRelation<T, TMutable>(
    rel: Relation<KootaValueTrait<T>>,
    isExclusive: boolean
  ): IRelation<IMutableValueTrait<T, TMutable>> {
    return fromKootaRelation(rel, false, isExclusive);
  }

  function CreateAdded(): IAddedTracker {
    const Added: KootaTracker = createAdded();
    return fromKootaAddedTracker(Added);
  }

  function CreateChanged(): IChangedTracker {
    const Changed: KootaTracker = createChanged();
    return fromKootaChangedTracker(Changed);
  }

  function CreateRemoved(): IRemovedTracker {
    const Removed: KootaTracker = createRemoved();
    return fromKootaRemovedTracker(Removed);
  }

  function Relation(config: RelationConfig): IRelation<ITagTrait> {
    // A storeless relation's underlying trait is Trait<Record<string, never>>, not TagTrait.
    const rel = config.IsExclusive ? relation({ exclusive: true }) : relation();
    return fromKootaTagRelation(rel, config.IsExclusive);
  }

  // We ignore the mutableStore parameter; It's only there for type inference on the F# side.
  function RelationWith<T, TMutable>(
    config: RelationConfig,
    store: T
  ): IRelation<IMutableValueTrait<T, TMutable>> {
    const typedStore = store as KootaSchema<T>;
    const rel = config.IsExclusive
      ? relation({ exclusive: true, store: typedStore })
      : relation({ store: typedStore });
    return fromKootaValueRelation(rel, config.IsExclusive);
  }

  function TagTrait(): ITagTrait {
    return fromKootaTagTrait(trait());
  }

  // We ignore the mutableValue parameter; It's only there for type inference on the F# side.
  function TraitWith<T, TMutable>(value: T): IMutableValueTrait<T, TMutable> {
    const traitDef = trait(value as KootaSchema<T>) as KootaValueTrait<T>;
    return fromKootaValueTrait(traitDef);
  }

  function TraitWithRef<T>(valueFactory: () => T): IMutableValueTrait<T, T> {
    return fromKootaValueFactoryTrait(trait(valueFactory));
  }

  return {
    CreateAdded,
    CreateChanged,
    CreateRemoved,
    Relation,
    RelationWith,
    TagTrait,
    TraitWith,
    TraitWithRef,
  };
}

type WrappedWorld = IWorld & { world: World };

export function fromKootaWorld(world: World): IWorld {
  function newWrapper(): WrappedWorld {
    function unwrapQueryOperators(ops: QueryOperator[]): QueryParameter[] {
      function toKootaTracker(tracker: ITracker): KootaTracker {
        if (!("kootaTracker" in tracker)) {
          throw new Error("Invalid ITracker implementation passed to toKootaTracker.");
        }
        const wrapper = tracker as WrappedTracker;
        return wrapper.kootaTracker;
      }

      return ops.map((op) => {
        switch (op.type) {
          case "with":
            return toKootaTrait(op.Item);
          case "not":
            const notOperands = Array.from(op.Item, toKootaTrait);
            return Not(...notOperands);
          case "or":
            const orOperands = Array.from(op.Item, toKootaTrait);
            return Or(...orOperands);
          case "added":
            const addedOperands = Array.from(op.Item1, toKootaTrait);
            const Added = toKootaTracker(op.Item2);
            return Added(...addedOperands);
          case "changed":
            const changedOperands = Array.from(op.Item1, toKootaTrait);
            const Changed = toKootaTracker(op.Item2);
            return Changed(...changedOperands);
          case "removed":
            const removedOperands = Array.from(op.Item1, toKootaTrait);
            const Removed = toKootaTracker(op.Item2);
            return Removed(...removedOperands);
        }
      });
    }

    function toKootaQueryResultOptions(changeOption: ChangeDetectionOption): QueryResultOptions {
      switch (changeOption.type) {
        case "autoTrack":
          return { changeDetection: "auto" };
        case "neverTrack":
          return { changeDetection: "never" };
        case "alwaysTrack":
          return { changeDetection: "always" };
      }
    }

    // NOTE:
    // For QueryResult, on the F# side, there are three cases for the T/TMutable type parameter that
    // don't quite map to TypeScript (we'll ignore the mutable type from here on for brevity):
    // 1. unit: Has a unit * EntityId callback, which maps to [undefined, EntityId] in TypeScript.
    // 2. T: Has a T * EntityId callback, which maps to [T, EntityId] in TypeScript.
    // 3. T * U (and future generalizations of higher arity): Was a (T * U) * EntityId callback,
    //    which maps to [[T, U], EntityId] in TypeScript.
    // In the functions below, we map state/trait values accordingly based on the given arity.
    function wrapVoidQueryResult<S>(
      result: QueryResult<KootaQueryParameters<S>>
    ): IQueryResult<void, void> {
      function ForEach(callback: (state: [void, EntityId]) => void): void {
        for (const entity of result) {
          callback([undefined, entity]);
        }
      }

      function UpdateEachWith(
        changeOption: ChangeDetectionOption,
        callback: (state: [void, EntityId]) => void
      ): void {
        function thunk(state: InstancesFromParameters<KootaQueryParameters<S>>, entity: Entity) {
          callback([undefined, entity]);
        }
        result.updateEach(thunk, toKootaQueryResultOptions(changeOption));
      }

      return {
        ForEach,
        UpdateEachWith,
        [Symbol.iterator](): Iterator<EntityId> {
          return result[Symbol.iterator]();
        },
      };
    }

    function wrapQueryResult1<S, T, TMutable>(
      result: QueryResult<KootaQueryParameters<S>>,
      valueTrait: KootaValueTrait<T>,
      updateEachError: string | undefined
    ): IQueryResult<T, TMutable> {
      function ForEach(callback: (state: [T, EntityId]) => void): void {
        // entity.get is correct for both plain value traits and relation pairs, including the
        // relation-only and non-exclusive cases where the iteration state can't surface the value.
        for (const entity of result) {
          const value = entity.get(valueTrait)!; // Trait must exist per the query that created this result.
          callback([value, entity]);
        }
      }

      function UpdateEachWith(
        changeOption: ChangeDetectionOption,
        callback: (state: [TMutable, EntityId]) => void
      ): void {
        if (updateEachError) {
          throw new Error(updateEachError);
        }
        function thunk(state: InstancesFromParameters<KootaQueryParameters<S>>, entity: Entity) {
          callback([state[0], entity]);
        }
        result.updateEach(thunk, toKootaQueryResultOptions(changeOption));
      }

      return {
        ForEach,
        UpdateEachWith,
        [Symbol.iterator](): Iterator<EntityId> {
          return result[Symbol.iterator]();
        },
      };
    }

    function wrapQueryResult2<S, T, TMutable, U, UMutable>(
      result: QueryResult<KootaQueryParameters<S>>,
      valueTraits: [KootaValueTrait<T>, KootaValueTrait<U>],
      updateEachError: string | undefined
    ): IQueryResult<[T, U], [TMutable, UMutable]> {
      function ForEach(callback: (state: [[T, U], EntityId]) => void): void {
        for (const entity of result) {
          const value1 = entity.get(valueTraits[0])!; // Traits must exist per the query that created this result.
          const value2 = entity.get(valueTraits[1])!;
          callback([[value1, value2], entity]);
        }
      }

      function UpdateEachWith(
        changeOption: ChangeDetectionOption,
        callback: (state: [[TMutable, UMutable], EntityId]) => void
      ): void {
        if (updateEachError) {
          throw new Error(updateEachError);
        }
        function thunk(state: InstancesFromParameters<KootaQueryParameters<S>>, entity: Entity) {
          callback([state.slice(0, 2) as [TMutable, UMutable], entity]);
        }
        result.updateEach(thunk, toKootaQueryResultOptions(changeOption));
      }

      return {
        ForEach,
        UpdateEachWith,
        [Symbol.iterator](): Iterator<EntityId> {
          return result[Symbol.iterator]();
        },
      };
    }

    function wrapQueryResult3<S, T, TMutable, U, UMutable, V, VMutable>(
      result: QueryResult<KootaQueryParameters<S>>,
      valueTraits: [KootaValueTrait<T>, KootaValueTrait<U>, KootaValueTrait<V>],
      updateEachError: string | undefined
    ): IQueryResult<[T, U, V], [TMutable, UMutable, VMutable]> {
      function ForEach(callback: (state: [[T, U, V], EntityId]) => void): void {
        for (const entity of result) {
          const value1 = entity.get(valueTraits[0])!; // Traits must exist per the query that created this result.
          const value2 = entity.get(valueTraits[1])!;
          const value3 = entity.get(valueTraits[2])!;
          callback([[value1, value2, value3], entity]);
        }
      }

      function UpdateEachWith(
        changeOption: ChangeDetectionOption,
        callback: (state: [[TMutable, UMutable, VMutable], EntityId]) => void
      ): void {
        if (updateEachError) {
          throw new Error(updateEachError);
        }
        function thunk(state: InstancesFromParameters<KootaQueryParameters<S>>, entity: Entity) {
          callback([state.slice(0, 3) as [TMutable, UMutable, VMutable], entity]);
        }
        result.updateEach(thunk, toKootaQueryResultOptions(changeOption));
      }

      return {
        ForEach,
        UpdateEachWith,
        [Symbol.iterator](): Iterator<EntityId> {
          return result[Symbol.iterator]();
        },
      };
    }

    function wrapQueryResult4<S, T, TMutable, U, UMutable, V, VMutable, W, WMutable>(
      result: QueryResult<KootaQueryParameters<S>>,
      valueTraits: [KootaValueTrait<T>, KootaValueTrait<U>, KootaValueTrait<V>, KootaValueTrait<W>],
      updateEachError: string | undefined
    ): IQueryResult<[T, U, V, W], [TMutable, UMutable, VMutable, WMutable]> {
      function ForEach(callback: (state: [[T, U, V, W], EntityId]) => void): void {
        for (const entity of result) {
          const value1 = entity.get(valueTraits[0])!; // Traits must exist per the query that created this result.
          const value2 = entity.get(valueTraits[1])!;
          const value3 = entity.get(valueTraits[2])!;
          const value4 = entity.get(valueTraits[3])!;
          callback([[value1, value2, value3, value4], entity]);
        }
      }

      function UpdateEachWith(
        changeOption: ChangeDetectionOption,
        callback: (state: [[TMutable, UMutable, VMutable, WMutable], EntityId]) => void
      ): void {
        if (updateEachError) {
          throw new Error(updateEachError);
        }
        function thunk(state: InstancesFromParameters<KootaQueryParameters<S>>, entity: Entity) {
          callback([state.slice(0, 4) as [TMutable, UMutable, VMutable, WMutable], entity]);
        }
        result.updateEach(thunk, toKootaQueryResultOptions(changeOption));
      }

      return {
        ForEach,
        UpdateEachWith,
        [Symbol.iterator](): Iterator<EntityId> {
          return result[Symbol.iterator]();
        },
      };
    }

    return new (class implements IWorld {
      readonly world: World = world;

      Add(someTrait: ITrait): void {
        this.world.add(toKootaTrait(someTrait));
      }

      Get<T>(valueTrait: IValueTrait<T>): Option<T> {
        return this.world.get(toKootaValueTraitForRead(valueTrait));
      }

      Has(someTrait: ITrait): boolean {
        return this.world.has(toKootaTrait(someTrait));
      }

      Query(...where: QueryOperator[]): IQueryResult<void, void> {
        const queryParameters = unwrapQueryOperators(where);
        const result = this.world.query(...queryParameters);
        return wrapVoidQueryResult(result);
      }

      QueryFirst(...where: QueryOperator[]): Option<EntityId> {
        const queryParameters = unwrapQueryOperators(where);
        return this.world.queryFirst(...queryParameters);
      }

      QueryTrait<T, TMutable>(
        someTrait: IMutableValueTrait<T, TMutable>,
        ...where: QueryOperator[]
      ): IQueryResult<T, TMutable> {
        const queryParameters = unwrapQueryOperators(where);
        const kootaValueTrait = toKootaValueTrait(someTrait);
        const result = this.world.query(kootaValueTrait, ...queryParameters);
        // A relation pair in the value slot can't surface its per-target value through iteration when
        // it's the sole query trait, or whenever it's non-exclusive. UpdateEach fails fast in both.
        const updateEachError =
          (queryParameters.length === 0 && isWrappedRelationPair(someTrait)) ||
          isNonExclusiveRelationPair(someTrait)
            ? relationValueUpdateEachError
            : undefined;
        return wrapQueryResult1(result, kootaValueTrait, updateEachError);
      }

      QueryTraits<T, TMutable, U, UMutable>(
        firstTrait: IMutableValueTrait<T, TMutable>,
        secondTrait: IMutableValueTrait<U, UMutable>,
        ...where: QueryOperator[]
      ): IQueryResult<[T, U], [TMutable, UMutable]> {
        const queryParameters = unwrapQueryOperators(where);
        const firstKootaValueTrait = toKootaValueTrait(firstTrait);
        const secondKootaValueTrait = toKootaValueTrait(secondTrait);
        const result = this.world.query(
          firstKootaValueTrait,
          secondKootaValueTrait,
          ...queryParameters
        );
        const updateEachError =
          isNonExclusiveRelationPair(firstTrait) || isNonExclusiveRelationPair(secondTrait)
            ? relationValueUpdateEachError
            : undefined;
        return wrapQueryResult2(
          result,
          [firstKootaValueTrait, secondKootaValueTrait],
          updateEachError
        );
      }

      QueryTraits3<T, TMutable, U, UMutable, V, VMutable>(
        firstTrait: IMutableValueTrait<T, TMutable>,
        secondTrait: IMutableValueTrait<U, UMutable>,
        thirdTrait: IMutableValueTrait<V, VMutable>,
        ...where: QueryOperator[]
      ): IQueryResult<[T, U, V], [TMutable, UMutable, VMutable]> {
        const queryParameters = unwrapQueryOperators(where);
        const firstKootaValueTrait = toKootaValueTrait(firstTrait);
        const secondKootaValueTrait = toKootaValueTrait(secondTrait);
        const thirdKootaValueTrait = toKootaValueTrait(thirdTrait);
        const result = this.world.query(
          firstKootaValueTrait,
          secondKootaValueTrait,
          thirdKootaValueTrait,
          ...queryParameters
        );
        const updateEachError =
          isNonExclusiveRelationPair(firstTrait) ||
          isNonExclusiveRelationPair(secondTrait) ||
          isNonExclusiveRelationPair(thirdTrait)
            ? relationValueUpdateEachError
            : undefined;
        return wrapQueryResult3(
          result,
          [firstKootaValueTrait, secondKootaValueTrait, thirdKootaValueTrait],
          updateEachError
        );
      }

      QueryTraits4<T, TMutable, U, UMutable, V, VMutable, W, WMutable>(
        firstTrait: IMutableValueTrait<T, TMutable>,
        secondTrait: IMutableValueTrait<U, UMutable>,
        thirdTrait: IMutableValueTrait<V, VMutable>,
        fourthTrait: IMutableValueTrait<W, WMutable>,
        ...where: QueryOperator[]
      ): IQueryResult<[T, U, V, W], [TMutable, UMutable, VMutable, WMutable]> {
        const queryParameters = unwrapQueryOperators(where);
        const firstKootaValueTrait = toKootaValueTrait(firstTrait);
        const secondKootaValueTrait = toKootaValueTrait(secondTrait);
        const thirdKootaValueTrait = toKootaValueTrait(thirdTrait);
        const fourthKootaValueTrait = toKootaValueTrait(fourthTrait);
        const result = this.world.query(
          firstKootaValueTrait,
          secondKootaValueTrait,
          thirdKootaValueTrait,
          fourthKootaValueTrait,
          ...queryParameters
        );
        const updateEachError =
          isNonExclusiveRelationPair(firstTrait) ||
          isNonExclusiveRelationPair(secondTrait) ||
          isNonExclusiveRelationPair(thirdTrait) ||
          isNonExclusiveRelationPair(fourthTrait)
            ? relationValueUpdateEachError
            : undefined;
        return wrapQueryResult4(
          result,
          [
            firstKootaValueTrait,
            secondKootaValueTrait,
            thirdKootaValueTrait,
            fourthKootaValueTrait,
          ],
          updateEachError
        );
      }

      Remove(someTrait: ITrait): void {
        return this.world.remove(toKootaTrait(someTrait));
      }

      Set<T>(valueTrait: IValueTrait<T>, value: T): void {
        const valueToSet = value as TraitValue<KootaSchema<T>>;
        world.set(toKootaValueTraitForRead(valueTrait), valueToSet);
      }

      Spawn(...traits: TraitSpec[]): EntityId {
        function unwrapValueSpec([traitWrapper, value]: [ITrait, unknown]): ConfigurableTrait<
          Trait<any>
        > {
          // A value-relation pair must be spawned in params form rel(target, value); a
          // [pair, value] tuple is rejected by Koota 0.6.x. Plain value traits still use the tuple.
          if (isWrappedRelationPair(traitWrapper)) {
            return traitWrapper.relationFn(
              traitWrapper.relationTarget,
              value as Record<string, unknown>
            ) as ConfigurableTrait<Trait<any>>;
          }
          return [toKootaTrait(traitWrapper), value] as ConfigurableTrait<Trait<any>>;
        }

        function unwrapTraitSpec(c: TraitSpec): ConfigurableTrait<Trait<any>> {
          return TraitSpec_Map(toKootaTrait, unwrapValueSpec, c);
        }

        return this.world.spawn(...traits.map(unwrapTraitSpec));
      }
    })();
  }

  return getOrCreateWrapper(world, () => newWrapper());
}

export function toKootaWorld(world: IWorld): World {
  if (!("world" in world)) {
    throw new Error("Invalid IWorld implementation passed to toKootaWorld.");
  }
  const wrapper = world as WrappedWorld;
  return wrapper.world;
}

export type { IWorld };
