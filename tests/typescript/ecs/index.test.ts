import { Matrix4 } from "three";
import { beforeEach, describe, expect, test, vi } from "vitest";
import { eventActions, runSystems, worldActions } from "../../../src/ecs/index";
import { fromKootaWorld } from "../../../src/ecs/koota/kootaWrapper";
import {
  handleClick,
  handleDrag,
  handleDragEnd,
  handleDragStart,
  handlePointerMissed,
} from "../../../src/generated/Traits/Events";
import { layoutNodes } from "../../../src/generated/Systems/Layout";
import { runSystems as runFableSystems } from "../../../src/generated/Systems/Runner";
import { spawnControls, spawnScene } from "../../../src/generated/EntityLifeCycle";

vi.mock("koota", async (importOriginal) => ({
  ...(await importOriginal()),
  createActions: (initializer: (world: unknown) => unknown) => (world: unknown) =>
    initializer(world),
}));

vi.mock("../../../src/ecs/koota/kootaWrapper", async (importOriginal) => ({
  ...(await importOriginal()),
  fromKootaWorld: vi.fn(),
}));

vi.mock("../../../src/generated/Systems/Layout", () => ({
  layoutNodes: vi.fn(),
}));

vi.mock("../../../src/generated/Systems/Runner", () => ({
  runSystems: vi.fn(),
}));

vi.mock("../../../src/generated/EntityLifeCycle", () => ({
  spawnControls: vi.fn(),
  spawnScene: vi.fn(),
}));

vi.mock("../../../src/generated/Traits/Events", () => ({
  handleClick: vi.fn(),
  handleDrag: vi.fn(),
  handleDragEnd: vi.fn(),
  handleDragStart: vi.fn(),
  handlePointerMissed: vi.fn(),
}));

describe("ecs bridge", () => {
  const world = { kind: "koota-world" };
  const wrappedWorld = { kind: "fsharp-world" };
  const familyGraph = { kind: "family-graph" };
  const entity = { id: 42 };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(fromKootaWorld).mockReturnValue(wrappedWorld as never);
  });

  test("runSystems wraps the world and delegates the delta", () => {
    const deltaSeconds = 0.125;

    runSystems({ world: world as never, delta: deltaSeconds });

    expect(fromKootaWorld).toHaveBeenCalledOnce();
    expect(fromKootaWorld).toHaveBeenCalledWith(world);
    expect(runFableSystems).toHaveBeenCalledOnce();
    expect(runFableSystems).toHaveBeenCalledWith(wrappedWorld, deltaSeconds);
  });

  test("world actions wrap once and delegate each action with the wrapped world", () => {
    const actions = worldActions(world as never);

    actions.layoutNodes(familyGraph as never);
    actions.spawnControls();
    actions.spawnScene(familyGraph as never);

    expect(fromKootaWorld).toHaveBeenCalledOnce();
    expect(fromKootaWorld).toHaveBeenCalledWith(world);
    expect(layoutNodes).toHaveBeenCalledOnce();
    expect(layoutNodes).toHaveBeenCalledWith(wrappedWorld, familyGraph);
    expect(spawnControls).toHaveBeenCalledOnce();
    expect(spawnControls).toHaveBeenCalledWith(wrappedWorld);
    expect(spawnScene).toHaveBeenCalledOnce();
    expect(spawnScene).toHaveBeenCalledWith(wrappedWorld, familyGraph);
  });

  test("handleClick action delegates the entity after wrapping the world", () => {
    const action = eventActions(world as never).handleClick(entity as never);

    action();

    expect(fromKootaWorld).toHaveBeenCalledOnce();
    expect(fromKootaWorld).toHaveBeenCalledWith(world);
    expect(handleClick).toHaveBeenCalledOnce();
    expect(handleClick).toHaveBeenCalledWith(wrappedWorld, entity);
  });

  test("handleDrag decomposes the matrix and delegates its translation coordinates", () => {
    const translationX = 1.25;
    const translationY = -2.5;
    const translationZ = 3.75;
    const matrix = new Matrix4().makeTranslation(translationX, translationY, translationZ);

    eventActions(world as never).handleDrag(matrix);

    expect(handleDrag).toHaveBeenCalledOnce();
    expect(handleDrag).toHaveBeenCalledWith(wrappedWorld, translationX, translationY, translationZ);
  });

  test.each([
    ["handleDragEnd", () => eventActions(world as never).handleDragEnd(), handleDragEnd],
    ["handleDragStart", () => eventActions(world as never).handleDragStart(), handleDragStart],
    [
      "handlePointerMissed",
      () => eventActions(world as never).handlePointerMissed(),
      handlePointerMissed,
    ],
  ])("%s delegates with the wrapped world", (_name, invoke, generatedAction) => {
    invoke();

    expect(generatedAction).toHaveBeenCalledOnce();
    expect(generatedAction).toHaveBeenCalledWith(wrappedWorld);
  });

  test("handleMeshClick handles the generated click before stopping propagation", () => {
    const callOrder: string[] = [];
    vi.mocked(handleClick).mockImplementation(() => {
      callOrder.push("handleClick");
    });
    const event = {
      stopPropagation: vi.fn(() => {
        callOrder.push("stopPropagation");
      }),
    };

    eventActions(world as never).handleMeshClick(entity as never)(event as never);

    expect(handleClick).toHaveBeenCalledOnce();
    expect(handleClick).toHaveBeenCalledWith(wrappedWorld, entity);
    expect(event.stopPropagation).toHaveBeenCalledOnce();
    expect(callOrder).toEqual(["handleClick", "stopPropagation"]);
  });
});
