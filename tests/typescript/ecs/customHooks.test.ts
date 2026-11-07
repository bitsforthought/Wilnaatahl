// @vitest-environment jsdom

import { createElement, forwardRef, useImperativeHandle, useLayoutEffect, useMemo } from "react";
import { act, cleanup, render, renderHook } from "@testing-library/react";
import { WorldProvider } from "koota/react";
import { createWorld, Entity, World } from "koota";
import { Mesh } from "three";
import { afterEach, describe, expect, test } from "vitest";
import { CurrentMode, LabelRef, MeshRef, Selected } from "../../../src/ecs";
import { useLabelRef, useMeshRef, useOverlayVisible } from "../../../src/ecs/customHooks";
import { movingMode, viewingMode } from "./appMode";

function worldWrapper(world: World) {
  return function WorldWrapper({ children }: { children?: React.ReactNode }) {
    return createElement(WorldProvider, { world, children });
  };
}

function MeshHost({ onMesh }: { onMesh: (mesh: Mesh) => void }, ref: React.ForwardedRef<Mesh>) {
  const mesh = useMemo(() => new Mesh(), []);
  useImperativeHandle(ref, () => mesh, [mesh]);
  useLayoutEffect(() => {
    onMesh(mesh);
  }, [mesh, onMesh]);
  return null;
}

const ForwardedMeshHost = forwardRef(MeshHost);

function MeshRefHarness({ entity, onMesh }: { entity: Entity; onMesh: (mesh: Mesh) => void }) {
  const meshRef = useMeshRef(entity);
  return createElement(ForwardedMeshHost, { onMesh, ref: meshRef });
}

function LabelRefHarness({ entity }: { entity: Entity }) {
  const labelRef = useLabelRef(entity);
  return createElement("div", { ref: labelRef });
}

function LabelRefWorldHarness({ world, entity }: { world: World; entity: Entity }) {
  return createElement(WorldProvider, {
    world,
    children: createElement(LabelRefHarness, { entity }),
  });
}

describe("useMeshRef", () => {
  let world: World;

  afterEach(() => {
    cleanup();
    world?.destroy();
  });

  test("attaches the host mesh and removes the MeshRef trait on unmount", () => {
    world = createWorld();
    const entity = world.spawn();
    let hostMesh: Mesh | undefined;
    const view = render(
      createElement(MeshRefHarness, {
        entity,
        onMesh: (mesh) => {
          hostMesh = mesh;
        },
      })
    );

    const mesh = entity.get(MeshRef);
    expect(mesh).toBe(hostMesh);

    view.unmount();

    expect(entity.has(MeshRef)).toBe(false);
  });

  test("moves the MeshRef trait when the entity changes", () => {
    world = createWorld();
    const firstEntity = world.spawn();
    const secondEntity = world.spawn();
    let hostMesh: Mesh | undefined;
    const view = render(
      createElement(MeshRefHarness, {
        entity: firstEntity,
        onMesh: (mesh) => {
          hostMesh = mesh;
        },
      })
    );

    view.rerender(
      createElement(MeshRefHarness, {
        entity: secondEntity,
        onMesh: (mesh) => {
          hostMesh = mesh;
        },
      })
    );

    expect(firstEntity.has(MeshRef)).toBe(false);
    expect(secondEntity.get(MeshRef)).toBe(hostMesh);
  });
});

describe("useLabelRef", () => {
  let world: World | undefined;

  afterEach(() => {
    cleanup();
    world?.destroy();
  });

  test("attaches a mounted label, initializes the measurement state, and removes it on unmount", () => {
    world = createWorld();
    const entity = world.spawn();
    const view = render(createElement(LabelRefWorldHarness, { world, entity }));

    const label = entity.get(LabelRef);
    expect(label).toBeInstanceOf(HTMLDivElement);
    expect(label?.dataset.labelMeasured).toBe("false");

    view.unmount();

    expect(entity.has(LabelRef)).toBe(false);
  });

  test("moves the label reference when the entity changes", () => {
    world = createWorld();
    const firstEntity = world.spawn();
    const secondEntity = world.spawn();
    const view = render(createElement(LabelRefWorldHarness, { world, entity: firstEntity }));

    view.rerender(createElement(LabelRefWorldHarness, { world, entity: secondEntity }));

    expect(firstEntity.has(LabelRef)).toBe(false);
    expect(secondEntity.get(LabelRef)).toBeInstanceOf(HTMLDivElement);
  });

  test("keeps separate worlds independent when their entity ids match", () => {
    const firstWorld = createWorld();
    const secondWorld = createWorld();
    const firstEntity = firstWorld.spawn();
    const secondEntity = secondWorld.spawn();
    const firstView = render(
      createElement(LabelRefWorldHarness, { world: firstWorld, entity: firstEntity })
    );
    const secondView = render(
      createElement(LabelRefWorldHarness, { world: secondWorld, entity: secondEntity })
    );

    expect(firstEntity.get(LabelRef)).not.toBe(secondEntity.get(LabelRef));

    firstView.unmount();
    secondView.unmount();
    firstWorld.destroy();
    secondWorld.destroy();
    world = undefined;
  });

  test("does not throw when a label callback arrives after entity destruction", () => {
    world = createWorld();
    const entity = world.spawn();
    const { result } = renderHook(() => useLabelRef(entity), {
      wrapper: worldWrapper(world),
    });
    const labelRef = result.current;

    entity.destroy();

    expect(() => act(() => labelRef(null))).not.toThrow();
  });

  test("does not throw when the world is destroyed before label unmount", () => {
    world = createWorld();
    const entity = world.spawn();
    const view = render(createElement(LabelRefWorldHarness, { world, entity }));

    world.destroy();
    world = undefined;

    expect(() => view.unmount()).not.toThrow();
  });

  test("does not remove a replacement world's label when an old ref unmounts", () => {
    world = createWorld();
    const oldEntity = world.spawn();
    const view = render(createElement(LabelRefWorldHarness, { world, entity: oldEntity }));

    world.destroy();
    world = undefined;

    const replacementWorld = createWorld();
    const replacementEntity = replacementWorld.spawn();
    const replacementLabel = document.createElement("div");
    replacementEntity.add(LabelRef(replacementLabel));

    expect(() => view.unmount()).not.toThrow();
    expect(replacementEntity.get(LabelRef)).toBe(replacementLabel);

    replacementWorld.destroy();
  });
});

describe("useOverlayVisible", () => {
  let world: World;

  afterEach(() => {
    cleanup();
    world?.destroy();
  });

  test("starts hidden without a mode and reacts when the mode is added", () => {
    world = createWorld();
    world.spawn(Selected);

    const { result } = renderHook(() => useOverlayVisible(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(false);

    act(() => {
      world.add(CurrentMode(viewingMode()));
    });

    expect(result.current).toBe(true);
  });

  test("shows only in viewing mode with exactly one selected entity", () => {
    world = createWorld();
    const selectedEntity = world.spawn();
    const secondSelectedEntity = world.spawn();
    world.add(CurrentMode(viewingMode()));

    const { result } = renderHook(() => useOverlayVisible(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(false);

    act(() => {
      selectedEntity.add(Selected);
    });
    expect(result.current).toBe(true);

    act(() => {
      secondSelectedEntity.add(Selected);
    });
    expect(result.current).toBe(false);

    act(() => {
      secondSelectedEntity.remove(Selected);
    });
    expect(result.current).toBe(true);

    act(() => {
      world.set(CurrentMode, movingMode());
    });
    expect(result.current).toBe(false);
  });

  test("reacts to mode changes while keeping the selection count unchanged", () => {
    world = createWorld();
    world.spawn(Selected);
    world.add(CurrentMode(movingMode()));

    const { result } = renderHook(() => useOverlayVisible(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(false);

    act(() => {
      world.set(CurrentMode, viewingMode());
    });
    expect(result.current).toBe(true);
  });
});
