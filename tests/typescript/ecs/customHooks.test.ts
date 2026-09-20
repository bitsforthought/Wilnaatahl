// @vitest-environment jsdom

import { createElement, forwardRef, useImperativeHandle, useLayoutEffect, useMemo } from "react";
import { act, cleanup, render, renderHook } from "@testing-library/react";
import { WorldProvider } from "koota/react";
import { createWorld } from "koota";
import { Mesh } from "three";
import { afterEach, describe, expect, test } from "vitest";
import {
  AppMode_Moving,
  AppMode_Viewing,
  isViewing,
} from "../../../src/generated/Traits/ViewTraits";
import { CurrentMode, MeshRef, Selected } from "../../../src/ecs";
import { useMeshRef, useOverlayVisible } from "../../../src/ecs/customHooks";

type World = ReturnType<typeof createWorld>;

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

function MeshRefHarness({
  entity,
  onMesh,
}: {
  entity: ReturnType<World["spawn"]>;
  onMesh: (mesh: Mesh) => void;
}) {
  const meshRef = useMeshRef(entity);
  return createElement(ForwardedMeshHost, { onMesh, ref: meshRef });
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

describe("useOverlayVisible", () => {
  let world: World;

  afterEach(() => {
    cleanup();
    world?.destroy();
  });

  test("starts hidden when mode and selection have not been established", () => {
    world = createWorld();

    const { result } = renderHook(() => useOverlayVisible(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(false);
  });

  test("shows only in viewing mode with exactly one selected entity", () => {
    world = createWorld();
    const selectedEntity = world.spawn();
    const secondSelectedEntity = world.spawn();
    world.add(CurrentMode(AppMode_Viewing()));

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
      world.set(CurrentMode, AppMode_Moving());
    });
    expect(result.current).toBe(false);
  });

  test("reacts to mode changes while keeping the selection count unchanged", () => {
    world = createWorld();
    const selectedEntity = world.spawn(Selected);
    world.add(CurrentMode(AppMode_Moving()));

    const { result } = renderHook(() => useOverlayVisible(), {
      wrapper: worldWrapper(world),
    });

    expect(result.current).toBe(false);

    act(() => {
      world.set(CurrentMode, AppMode_Viewing());
    });

    expect(result.current).toBe(true);
    expect(isViewing(world.get(CurrentMode)!)).toBe(true);
  });
});
