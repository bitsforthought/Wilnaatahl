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
import { isViewing } from "../../../src/generated/Traits/ViewTraits";
import { Locale } from "../../../src/generated/ViewModel/Localization";
import { NodeLabelView_get_Empty } from "../../../src/generated/ViewModel/NodeContent";
import { movingMode, viewingMode } from "./appMode";

test.each([
  ["Viewing", viewingMode, true],
  ["Moving", movingMode, false],
])("%s adapter returns the intended mode", (_name, createMode, expectedViewing) => {
  expect(isViewing(createMode())).toBe(expectedViewing);
});

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

  test("reference traits preserve canonical defaults and independent label records", () => {
    const first = world.spawn(CurrentMode(), CurrentLocale(), NodeLabel());
    const second = world.spawn(CurrentMode(), CurrentLocale(), NodeLabel());

    expect(first.get(CurrentMode)).toStrictEqual(viewingMode());
    expect(second.get(CurrentMode)).toStrictEqual(viewingMode());
    expect(first.get(CurrentLocale)).toStrictEqual(new Locale());
    expect(second.get(CurrentLocale)).toStrictEqual(new Locale());
    expect(first.get(NodeLabel)).toStrictEqual(NodeLabelView_get_Empty());
    expect(second.get(NodeLabel)).toStrictEqual(NodeLabelView_get_Empty());
    expect(first.get(NodeLabel)).not.toBe(second.get(NodeLabel));
  });

  test("changing one entity's mode leaves another entity's mode unchanged", () => {
    const first = world.spawn(CurrentMode());
    const second = world.spawn(CurrentMode());

    first.set(CurrentMode, movingMode());

    expect(first.get(CurrentMode)).toStrictEqual(movingMode());
    expect(second.get(CurrentMode)).toStrictEqual(viewingMode());
  });

  test("factory traits retain explicit union and record values", () => {
    const explicitMode = movingMode();
    const person = Person_get_Empty();
    const entity = world.spawn(CurrentMode(explicitMode), PersonRef(person));

    expect(entity.get(CurrentMode)).toBe(explicitMode);
    expect(entity.get(PersonRef)).toBe(person);
  });
});
