import { createWorld, Entity } from "koota";
import { Vector3 } from "three";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import { getLinePositions } from "../../../src/ecs/connectors";
import { fromKootaWorld } from "../../../src/ecs/koota/kootaWrapper";
import { EntityId } from "../../../src/generated/ECS/Types";
import { spawn as spawnLine } from "../../../src/generated/Entities/Line";

describe("getLinePositions", () => {
  let world: ReturnType<typeof createWorld>;

  beforeEach(() => {
    world = createWorld();
  });

  afterEach(() => {
    world.destroy();
  });

  function lineBetween(
    first: { x: number; y: number; z: number },
    second: { x: number; y: number; z: number }
  ) {
    return spawnLine(first, second, fromKootaWorld(world)) as Entity & EntityId;
  }

  test("returns both stored endpoint positions as Three.js vectors", () => {
    const line = lineBetween({ x: 1, y: 2, z: 3 }, { x: -4, y: 5, z: -6 });
    const result = getLinePositions(world, line);

    expect(result).toEqual([new Vector3(1, 2, 3), new Vector3(-4, 5, -6)]);
    expect(result[0]).toBeInstanceOf(Vector3);
    expect(result[1]).toBeInstanceOf(Vector3);
  });

  test("preserves zero and fractional coordinate boundaries", () => {
    const line = lineBetween(
      { x: 0, y: 0.5, z: -0.5 },
      { x: Number.MIN_VALUE, y: Number.MAX_VALUE, z: -Number.MAX_VALUE }
    );

    expect(getLinePositions(world, line)).toEqual([
      new Vector3(0, 0.5, -0.5),
      new Vector3(Number.MIN_VALUE, Number.MAX_VALUE, -Number.MAX_VALUE),
    ]);
  });
});
