import { createWorld, World } from "koota";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import {
  createEntityOperations,
  createTraitFactory,
  fromKootaWorld,
  toKootaRelation,
} from "../../../src/ecs/koota/kootaWrapper";
import { SpawnSpec_Val } from "../../../src/generated/ECS/Types";

function expectExactError(action: () => unknown, expectedMessage: string): void {
  let thrown: unknown;
  try {
    action();
  } catch (error) {
    thrown = error;
  }

  expect(thrown).toBeInstanceOf(Error);
  expect((thrown as Error).message).toBe(expectedMessage);
}

describe("Koota wrapper contracts", () => {
  let world: World;

  beforeEach(() => {
    world = createWorld();
  });

  afterEach(() => {
    world.destroy();
  });

  test("iterates query results for two, three, and four value traits", () => {
    const factory = createTraitFactory();
    const first = factory.TraitWith({ value: 1 }, { value: 1 });
    const second = factory.TraitWith({ value: 2 }, { value: 2 });
    const third = factory.TraitWith({ value: 3 }, { value: 3 });
    const fourth = factory.TraitWith({ value: 4 }, { value: 4 });
    const wrappedWorld = fromKootaWorld(world);
    const entity = wrappedWorld.Spawn(
      SpawnSpec_Val([first, { value: 1 }]),
      SpawnSpec_Val([second, { value: 2 }]),
      SpawnSpec_Val([third, { value: 3 }]),
      SpawnSpec_Val([fourth, { value: 4 }])
    );

    const twoTraitValues: unknown[] = [];
    const threeTraitValues: unknown[] = [];
    const fourTraitValues: unknown[] = [];
    const twoTraitResult = wrappedWorld.QueryTraits(first, second);
    const threeTraitResult = wrappedWorld.QueryTraits3(first, second, third);
    const fourTraitResult = wrappedWorld.QueryTraits4(first, second, third, fourth);

    twoTraitResult.ForEach(([values, queriedEntity]) =>
      twoTraitValues.push([values, queriedEntity])
    );
    threeTraitResult.ForEach(([values, queriedEntity]) =>
      threeTraitValues.push([values, queriedEntity])
    );
    fourTraitResult.ForEach(([values, queriedEntity]) =>
      fourTraitValues.push([values, queriedEntity])
    );

    expect([...twoTraitResult]).toEqual([entity]);
    expect([...threeTraitResult]).toEqual([entity]);
    expect([...fourTraitResult]).toEqual([entity]);
    expect(twoTraitValues).toEqual([[[{ value: 1 }, { value: 2 }], entity]]);
    expect(threeTraitValues).toEqual([[[{ value: 1 }, { value: 2 }, { value: 3 }], entity]]);
    expect(fourTraitValues).toEqual([
      [[{ value: 1 }, { value: 2 }, { value: 3 }, { value: 4 }], entity],
    ]);
  });

  test("rejects an invalid trait implementation with the operation contract", () => {
    const entity = world.spawn();
    const operations = createEntityOperations();

    expectExactError(
      () => operations.Add({ IsTag: false } as never, entity),
      "Invalid ITrait implementation passed to toKootaTrait()."
    );
  });

  test("rejects invalid relation and tracker implementations with contract messages", () => {
    expectExactError(
      () => toKootaRelation({} as never),
      "Invalid IRelation implementation passed to toKootaRelation()."
    );

    const wrappedWorld = fromKootaWorld(world);
    const trait = createTraitFactory().TagTrait();

    expectExactError(
      () =>
        wrappedWorld.Query({
          type: "added",
          Item1: [trait],
          Item2: {} as never,
        }),
      "Invalid ITracker implementation passed to toKootaTracker."
    );
  });
});
