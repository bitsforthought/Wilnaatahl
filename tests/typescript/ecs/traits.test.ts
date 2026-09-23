import { createWorld, World } from "koota";
import { afterEach, beforeEach, describe, expect, test } from "vitest";
import { Mesh } from "three";
import {
  Button,
  CurrentLocale,
  CurrentMode,
  DragInFlight,
  Elbow,
  Hidden,
  Line,
  MeshRef,
  NodeLabel,
  OpenFileRequested,
  PersonRef,
  Position,
  SaveRequested,
  Selected,
  Size,
} from "../../../src/ecs/traits";
import { Person_get_Empty } from "../../../src/generated/Model";
import { AppMode_Moving } from "../../../src/generated/Traits/ViewTraits";

describe("ECS trait declarations", () => {
  let world: World;

  beforeEach(() => {
    world = createWorld();
  });

  afterEach(() => {
    world.destroy();
  });

  test.each([
    ["OpenFileRequested", OpenFileRequested],
    ["SaveRequested", SaveRequested],
    ["Elbow", Elbow],
    ["Line", Line],
    ["DragInFlight", DragInFlight],
    ["Hidden", Hidden],
    ["Selected", Selected],
  ])("%s is a removable tag trait", (_name, tagTrait) => {
    const entity = world.spawn();

    expect(entity.has(tagTrait)).toBe(false);

    entity.add(tagTrait);
    expect(entity.has(tagTrait)).toBe(true);

    entity.remove(tagTrait);
    expect(entity.has(tagTrait)).toBe(false);
  });

  test("MeshRef creates an independent Three.js mesh for each entity", () => {
    const first = world.spawn(MeshRef());
    const second = world.spawn(MeshRef());

    const firstMesh = first.get(MeshRef);
    const secondMesh = second.get(MeshRef);

    expect(firstMesh).toBeInstanceOf(Mesh);
    expect(secondMesh).toBeInstanceOf(Mesh);
    expect(firstMesh).not.toBe(secondMesh);
  });

  test("value traits preserve their configured values on entities", () => {
    const position = { x: 1.5, y: -2, z: 3 };
    const size = { x: 4, y: 5, z: 6 };
    const button = { disabled: true, label: "Save", sortOrder: 7 };
    const person = Person_get_Empty();

    const entity = world.spawn(Position(position), Size(size), Button(button), PersonRef(person));

    expect(entity.get(Position)).toEqual(position);
    expect(entity.get(Size)).toEqual(size);
    expect(entity.get(Button)).toEqual(button);
    expect(entity.get(PersonRef)).toBe(person);
  });

  test("Position factory values are zeroed and independent per entity", () => {
    const first = world.spawn(Position());
    const second = world.spawn(Position());
    const firstPosition = first.get(Position)!;
    const secondPosition = second.get(Position)!;

    expect(firstPosition).toStrictEqual({ x: 0, y: 0, z: 0 });
    expect(secondPosition).toStrictEqual({ x: 0, y: 0, z: 0 });
    expect(firstPosition).not.toBe(secondPosition);

    firstPosition.x = 9;
    expect(secondPosition.x).toBe(0);
  });

  test("reference traits use fresh defaults rather than sharing mutable values", () => {
    const first = world.spawn(CurrentMode(), CurrentLocale(), NodeLabel());
    const second = world.spawn(CurrentMode(), CurrentLocale(), NodeLabel());

    expect(first.get(CurrentMode)).toEqual(second.get(CurrentMode));
    expect(first.get(CurrentMode)).not.toBe(second.get(CurrentMode));
    expect(first.get(CurrentLocale)).toEqual(second.get(CurrentLocale));
    expect(first.get(CurrentLocale)).not.toBe(second.get(CurrentLocale));
    expect(first.get(NodeLabel)).toEqual(second.get(NodeLabel));
    expect(first.get(NodeLabel)).not.toBe(second.get(NodeLabel));
  });

  test("factory traits retain explicit union and record values", () => {
    const movingMode = AppMode_Moving();
    const person = Person_get_Empty();
    const entity = world.spawn(CurrentMode(movingMode), PersonRef(person));

    expect(entity.get(CurrentMode)).toBe(movingMode);
    expect(entity.get(PersonRef)).toBe(person);
  });
});
