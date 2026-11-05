import { createWorld } from "koota";
import { Camera, Mesh } from "three";
import { describe, expect, test } from "vitest";
import {
  createLabelRegistry,
  hideLabelsWithFSharp,
  readRenderedNode,
  replaceLabelRegistration,
  runLabelOcclusionPass,
  type LabelOcclusionInput,
  type LabelRegistry,
  type RenderedNode,
} from "../../../src/react-components/labelOcclusion";
import { MeshRef } from "../../../src/ecs";

type TestElement = HTMLElement & {
  emitResize(width: number, height: number): void;
  visibilityWrites: number;
};

type TestResizeObserver = {
  observe(element: HTMLElement): void;
  disconnect(): void;
  emit(width: number, height: number): void;
  emitEntries(entries: ResizeObserverEntry[]): void;
};

function createTestElement(): { element: TestElement; observer: TestResizeObserver } {
  let resizeCallback: ResizeObserverCallback | undefined;
  let visibility = "";
  let visibilityWrites = 0;
  let transform = "";
  const element = {
    get visibilityWrites() {
      return visibilityWrites;
    },
    get style() {
      return {
        get visibility() {
          return visibility;
        },
        set visibility(value: string) {
          visibilityWrites += 1;
          visibility = value;
        },
        get transform() {
          return transform;
        },
        set transform(value: string) {
          transform = value;
        },
      };
    },
    emitResize(width: number, height: number) {
      resizeCallback?.(
        [{ contentRect: { width, height } } as ResizeObserverEntry],
        {} as ResizeObserver
      );
    },
  } as TestElement;
  const observer = {
    observe() {
      return undefined;
    },
    disconnect() {
      return undefined;
    },
    emit(width: number, height: number) {
      element.emitResize(width, height);
    },
    emitEntries(entries: ResizeObserverEntry[]) {
      resizeCallback?.(entries, {} as ResizeObserver);
    },
  };
  (
    element as TestElement & { setResizeCallback(callback: ResizeObserverCallback): void }
  ).setResizeCallback = (callback) => {
    resizeCallback = callback;
  };
  return { element, observer };
}

function createRegistry(
  elements: TestElement[],
  onSizeChanged?: (id: number | string, size: { width: number; height: number }) => void
): {
  registry: LabelRegistry;
  observers: TestResizeObserver[];
} {
  const observers: TestResizeObserver[] = [];
  const registry = createLabelRegistry({
    createResizeObserver(callback) {
      const { observer, element } = createTestElement();
      elements.push(element);
      (
        element as TestElement & { setResizeCallback(callback: ResizeObserverCallback): void }
      ).setResizeCallback(callback);
      observers.push(observer);
      return observer;
    },
    onSizeChanged,
  });
  return { registry, observers };
}

function renderedNode(id: number): RenderedNode {
  return { id, mesh: new Mesh() };
}

describe("label occlusion registry", () => {
  test("converts plain browser inputs to F# occlusion results", () => {
    const inputs: LabelOcclusionInput[] = [
      {
        id: 1,
        bounds: { left: 0, right: 300, top: 0, bottom: 300 },
        viewSpaceDepth: 2,
        labelRect: undefined,
      },
      {
        id: 2,
        bounds: { left: 20, right: 40, top: 20, bottom: 40 },
        viewSpaceDepth: 4,
        labelRect: { left: 250, right: 350, top: 250, bottom: 350 },
      },
    ];

    expect(hideLabelsWithFSharp(inputs)).toEqual([2]);
  });

  test("reads a rendered node from its entity mesh trait", () => {
    const world = createWorld();
    const mesh = new Mesh();
    const entity = world.spawn(MeshRef(mesh));

    expect(readRenderedNode(entity)).toEqual({ id: entity.id(), mesh });

    world.destroy();
  });

  test("rejects a rendered node without a mesh trait", () => {
    const world = createWorld();
    const entity = world.spawn();

    expect(() => readRenderedNode(entity)).toThrowError(
      new Error(`Rendered node ${entity.id()} has no mesh.`)
    );

    world.destroy();
  });

  test("replaces a label registration without retaining the old element", () => {
    const elements: TestElement[] = [];
    const { registry } = createRegistry(elements);
    const firstMesh = new Mesh();
    const secondMesh = new Mesh();
    const firstElement = createTestElement().element;
    const secondElement = createTestElement().element;
    const firstCleanup = replaceLabelRegistration(undefined, registry, 3, firstMesh, firstElement);
    const secondCleanup = replaceLabelRegistration(
      firstCleanup,
      registry,
      3,
      secondMesh,
      secondElement
    );

    expect(registry.readElement(3)).toBe(secondElement);
    replaceLabelRegistration(secondCleanup, registry, 3, null, null);
    expect(registry.has(3)).toBe(false);
  });

  test("ignores incomplete and stale resize notifications", () => {
    const elements: TestElement[] = [];
    const { registry, observers } = createRegistry(elements);
    const first = createTestElement().element;
    registry.register(30, new Mesh(), first);
    const observer = observers[0];
    if (!observer) throw new Error("The label observer was not registered.");

    observer.emitEntries([]);
    observer.emit(0, 20);
    observer.emitEntries([
      { contentRect: { width: Number.NaN, height: 20 } } as ResizeObserverEntry,
    ]);
    expect(registry.readSize(30)).toBeUndefined();

    registry.register(30, new Mesh(), createTestElement().element);
    observer.emit(40, 10);
    expect(registry.readSize(30)).toBeUndefined();
  });

  test("caches changed unscaled sizes and keeps hidden labels measurable", () => {
    const elements: TestElement[] = [];
    const sizeChanges: Array<{ width: number; height: number }> = [];
    const { registry, observers } = createRegistry(elements, (_id, size) => {
      sizeChanges.push(size);
    });
    const element = createTestElement().element;
    Object.defineProperty(element, "dataset", { configurable: true, value: {} });
    const cleanup = registry.register(10, new Mesh(), element);

    expect(element.style.visibility).toBe("hidden");
    const observer = observers[0];
    if (!observer) throw new Error("The registration did not create an observer.");

    observer.emit(120, 24);
    expect(registry.readSize(10)).toEqual({ width: 120, height: 24 });
    expect(sizeChanges).toEqual([{ width: 120, height: 24 }]);
    const writesAfterFirstMeasurement = element.visibilityWrites;

    observer.emit(120, 24);
    expect(registry.readSize(10)).toEqual({ width: 120, height: 24 });
    expect(element.visibilityWrites).toBe(writesAfterFirstMeasurement);

    cleanup();
    expect(registry.readSize(10)).toBeUndefined();
  });

  test("re-registration and duplicate person occurrences stay keyed by rendered entity id", () => {
    const elements: TestElement[] = [];
    const { registry } = createRegistry(elements);
    const first = createTestElement().element;
    const replacement = createTestElement().element;
    const firstCleanup = registry.register(20, new Mesh(), first);
    registry.register(20, new Mesh(), replacement);
    firstCleanup();

    expect(registry.has(20)).toBe(true);
    expect(registry.readElement(20)).toBe(replacement);

    registry.register(21, new Mesh(), first);
    expect(registry.size).toBe(2);
    expect(registry.readElement(21)).toBe(first);
    expect(registry.readElement(20)).toBe(replacement);
  });

  test("reads only a matching placement cache and handles missing registrations", () => {
    const elements: TestElement[] = [];
    const { registry } = createRegistry(elements);
    const mesh = new Mesh();
    const camera = {} as Camera;
    const element = createTestElement().element;
    registry.register(40, mesh, element);

    expect(registry.readByMesh(new Mesh())).toBeUndefined();
    expect(registry.readPlacement(mesh, camera, 800, 600)).toBeUndefined();
    expect(registry.readPlacement(new Mesh(), camera, 800, 600)).toBeUndefined();

    const placement = { x: 10, y: 20, scale: 0.5 };
    registry.setPlacement(40, camera, 800, 600, placement);
    expect(registry.readPlacement(mesh, camera, 800, 600)).toBe(placement);
    expect(element.style.transform).toBe("translateX(-50%) scale(0.5)");
    expect(registry.readPlacement(mesh, camera, 801, 600)).toBeUndefined();
    registry.setPlacement(999, camera, 800, 600, placement);
    registry.setVisibility(999, true);

    registry.prune(new Set(["missing"]));
    expect(registry.has(40)).toBe(false);

    const sharedMesh = new Mesh();
    registry.register(43, sharedMesh, createTestElement().element);
    registry.register(44, sharedMesh, createTestElement().element);
    registry.prune(new Set(["missing"]));
    expect(registry.size).toBe(0);
  });

  test("uses the browser ResizeObserver fallback when no factory is supplied", () => {
    const globalObject = globalThis as typeof globalThis & {
      ResizeObserver?: typeof ResizeObserver;
    };
    const previousDescriptor = Object.getOwnPropertyDescriptor(globalObject, "ResizeObserver");
    const observed: HTMLElement[] = [];
    class TestBrowserResizeObserver {
      constructor() {}
      observe(element: HTMLElement) {
        observed.push(element);
      }
      disconnect() {
        return undefined;
      }
    }

    Object.defineProperty(globalObject, "ResizeObserver", {
      configurable: true,
      value: TestBrowserResizeObserver,
    });
    try {
      const registry = createLabelRegistry();
      const element = createTestElement().element;
      registry.register(41, new Mesh(), element);
      expect(observed).toEqual([element]);
    } finally {
      if (previousDescriptor) {
        Object.defineProperty(globalObject, "ResizeObserver", previousDescriptor);
      } else {
        Reflect.deleteProperty(globalObject, "ResizeObserver");
      }
    }
  });

  test("uses no observer when the browser does not provide ResizeObserver", () => {
    const globalObject = globalThis as typeof globalThis & {
      ResizeObserver?: typeof ResizeObserver;
    };
    const previousDescriptor = Object.getOwnPropertyDescriptor(globalObject, "ResizeObserver");
    if (previousDescriptor) Reflect.deleteProperty(globalObject, "ResizeObserver");
    try {
      const registry = createLabelRegistry();
      const element = createTestElement().element;
      const cleanup = registry.register(42, new Mesh(), element);
      expect(registry.readSize(42)).toBeUndefined();
      cleanup();
    } finally {
      if (previousDescriptor) {
        Object.defineProperty(globalObject, "ResizeObserver", previousDescriptor);
      }
    }
  });

  test("builds one input per rendered node, derives measured label rectangles, and writes visibility only on changes", () => {
    const elements: TestElement[] = [];
    const { registry, observers } = createRegistry(elements);
    const labelElement = createTestElement().element;
    const nodes = [renderedNode(1), renderedNode(2)];
    registry.register(2, nodes[1].mesh, labelElement);
    const resizeObserver = observers[0];
    if (!resizeObserver) throw new Error("The label observer was not registered.");
    resizeObserver.emit(100, 20);
    labelElement.style.visibility = "hidden";
    const bounds = new Map([
      [1, { left: 0, right: 40, top: 0, bottom: 40 }],
      [2, { left: 20, right: 60, top: 20, bottom: 60 }],
    ]);
    const depths = new Map([
      [1, 2],
      [2, 4],
    ]);
    const received: LabelOcclusionInput[][] = [];
    const inputOptions = {
      registry,
      nodes,
      camera: {} as Camera,
      canvasWidth: 800,
      canvasHeight: 600,
      projectNodeBounds: (node: RenderedNode) => bounds.get(node.id)!,
      readViewSpaceDepth: (node: RenderedNode) => depths.get(node.id)!,
      readPixelsPerWorldUnit: () => 1,
      hideLabels: (inputs: readonly LabelOcclusionInput[]) => {
        received.push([...inputs]);
        return [2];
      },
    };

    runLabelOcclusionPass(inputOptions);

    expect(received).toHaveLength(1);
    expect(received[0]).toEqual([
      {
        id: 1,
        bounds: bounds.get(1),
        viewSpaceDepth: 2,
        labelRect: undefined,
      },
      {
        id: 2,
        bounds: bounds.get(2),
        viewSpaceDepth: 4,
        labelRect: {
          left: 39.375,
          right: 40.625,
          top: 68,
          bottom: 68.25,
        },
      },
    ]);
    expect(labelElement.style.visibility).toBe("hidden");
    const writesAfterOcclusion = labelElement.visibilityWrites;

    runLabelOcclusionPass({ ...inputOptions, hideLabels: () => [] });

    expect(labelElement.style.visibility).toBe("visible");
    expect(labelElement.visibilityWrites).toBe(writesAfterOcclusion + 1);
    runLabelOcclusionPass({ ...inputOptions, hideLabels: () => [] });
    expect(labelElement.visibilityWrites).toBe(writesAfterOcclusion + 1);
  });

  test("uses the default Three.js projections and keeps an unmeasured label hidden", () => {
    const registry = createLabelRegistry({ createResizeObserver: () => undefined });
    const mesh = new Mesh();
    const element = {
      dataset: {},
      style: { visibility: "" },
    } as unknown as HTMLElement;
    registry.register(50, mesh, element);
    const camera = new Camera();
    camera.updateMatrixWorld();
    const received: LabelOcclusionInput[][] = [];

    runLabelOcclusionPass({
      registry,
      nodes: [{ id: 50, mesh }],
      camera,
      canvasWidth: 800,
      canvasHeight: 600,
      hideLabels: (inputs) => {
        received.push([...inputs]);
        return [];
      },
    });

    expect(received).toHaveLength(1);
    expect(received[0]?.[0]?.id).toBe(50);
    expect(received[0]?.[0]?.labelRect).toBeUndefined();
    expect(element.style.visibility).toBe("hidden");

    runLabelOcclusionPass({
      registry,
      nodes: [],
      camera,
      canvasWidth: 800,
      canvasHeight: 600,
      hideLabels: () => [],
    });
    expect(registry.size).toBe(0);
  });
});
