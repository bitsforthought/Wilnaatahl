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
  IRelation,
  IRelation$2 as IRelationValue,
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

// A wrapped relation carries the underlying Koota relation function so we can build a relation pair
// (rel(target)) on demand for entity ops and target-specific queries. A relation is a query filter
// plus a per-(subject, target) value store, not a trait, so it never flows through the trait wrappers.
type WrappedRelation = IRelation & {
  rel: Relation<Trait<any>>;
};

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

export function toKootaRelation(r: IRelation): Relation<Trait<any>> {
  if (!("rel" in r)) {
    throw new Error("Invalid IRelation implementation passed to toKootaRelation().");
  }
  return (r as WrappedRelation).rel;
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

  function AddRelation<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
    target: EntityId,
    entity: EntityId & Entity
  ): void {
    entity.add(toKootaRelation(relation)(target as Entity));
  }

  function RemoveRelation<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
    target: EntityId,
    entity: EntityId & Entity
  ): void {
    entity.remove(toKootaRelation(relation)(target as Entity));
  }

  function HasRelation<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
    target: EntityId,
    entity: EntityId & Entity
  ): boolean {
    return entity.has(toKootaRelation(relation)(target as Entity));
  }

  function GetRelationValue<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
    target: EntityId,
    entity: EntityId & Entity
  ): Option<T> {
    return entity.get(toKootaRelation(relation)(target as Entity)) as Option<T>;
  }

  function SetRelationValue<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
    target: EntityId,
    value: T,
    entity: EntityId & Entity
  ): void {
    const pair = toKootaRelation(relation)(target as Entity);
    // Guard against Koota's phantom write: entity.set() on a pair the subject never had would
    // write into the relation store regardless (setRelationDataAtIndex does store[eid] = value),
    // silently corrupting state. The documented contract is to throw. The message prefix (up to the
    // entity id) is the cross-backend contract, mirrored verbatim in the .NET mock
    // (src/Wilnaatahl.Core/ECS/Mocks/TestECS.fs); the trailing entity id is non-deterministic across
    // backends and not part of it.
    if (!entity.has(pair)) {
      throw new Error(
        `Cannot set a value for a relation that is not present on the subject entity ${entity.id()}`
      );
    }
    entity.set(pair, value as TraitValue<Schema>);
  }

  function TargetFor<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
    entity: EntityId & Entity
  ): Option<EntityId> {
    return entity.targetFor(toKootaRelation(relation));
  }

  function TargetsFor<T, TMutable>(
    relation: IRelationValue<T, TMutable>,
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
    AddRelation,
    RemoveRelation,
    HasRelation,
    GetRelationValue,
    SetRelationValue,
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

  function fromKootaRelation(rel: Relation<Trait<any>>, isExclusive: boolean): WrappedRelation {
    // The relation function is a stable object created once per Relation/RelationWith call, so we
    // cache the wrapper on it. A relation is a query filter plus a per-(subject, target) value
    // store, not a trait, so it never flows through the trait wrappers or produces a pair here;
    // pairs are built on demand (rel(target)) by entity ops and target-specific queries.
    return getOrCreateWrapper(rel, (r) => ({ IsExclusive: isExclusive, rel: r }));
  }

  // IRelationValue<T, TMutable> adds nothing structurally to IRelation (its T/TMutable are phantom
  // type parameters used only for F#-side inference), and WrappedRelation already extends IRelation,
  // so the assertion only supplies the phantom generics — it erases no runtime member.
  function fromKootaTagRelation(
    rel: Relation<Trait<any>>,
    isExclusive: boolean
  ): IRelationValue<void, void> {
    return fromKootaRelation(rel, isExclusive) as IRelationValue<void, void>;
  }

  function fromKootaValueRelation<T, TMutable>(
    rel: Relation<KootaValueTrait<T>>,
    isExclusive: boolean
  ): IRelationValue<T, TMutable> {
    return fromKootaRelation(rel as Relation<Trait<any>>, isExclusive) as IRelationValue<
      T,
      TMutable
    >;
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

  function Relation(config: RelationConfig): IRelationValue<void, void> {
    // A storeless relation's underlying trait is Trait<Record<string, never>>, not TagTrait.
    const rel = config.IsExclusive ? relation({ exclusive: true }) : relation();
    return fromKootaTagRelation(rel, config.IsExclusive);
  }

  // We ignore the mutableStore parameter; It's only there for type inference on the F# side.
  function RelationWith<T, TMutable>(
    config: RelationConfig,
    store: T
  ): IRelationValue<T, TMutable> {
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
          case "related":
            return toKootaRelation(op.Item1)(op.Item2 as Entity);
          case "relatedToAny":
            return toKootaRelation(op.Item)("*");
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
      valueTrait: KootaValueTrait<T>
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
      valueTraits: [KootaValueTrait<T>, KootaValueTrait<U>]
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
      valueTraits: [KootaValueTrait<T>, KootaValueTrait<U>, KootaValueTrait<V>]
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
      valueTraits: [KootaValueTrait<T>, KootaValueTrait<U>, KootaValueTrait<V>, KootaValueTrait<W>]
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
        return wrapQueryResult1(result, kootaValueTrait);
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
        return wrapQueryResult2(result, [firstKootaValueTrait, secondKootaValueTrait]);
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
        return wrapQueryResult3(result, [
          firstKootaValueTrait,
          secondKootaValueTrait,
          thirdKootaValueTrait,
        ]);
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
        return wrapQueryResult4(result, [
          firstKootaValueTrait,
          secondKootaValueTrait,
          thirdKootaValueTrait,
          fourthKootaValueTrait,
        ]);
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
