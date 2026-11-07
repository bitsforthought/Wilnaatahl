import type { Camera, Object3D } from "three";
import type { Entity } from "koota";
import { MeshRef } from "../ecs";
import {
  LabelOcclusionInput as FSharpLabelOcclusionInput,
  LabelPlacementModule_hiddenLabels,
  ProjectedNodeBounds as FSharpProjectedNodeBounds,
  ScreenRect as FSharpScreenRect,
} from "../generated/ViewModel/LabelPlacement";
import {
  projectNodeBounds,
  readPixelsPerWorldUnit,
  readViewSpaceDepth,
  type ProjectedNodeBounds,
} from "./anchor";
import { projectLabelPlacementFromBounds, type LabelPlacementLike } from "./labelPlacement";

export type LabelRegistryKey = number | string;

export type LabelSize = {
  width: number;
  height: number;
};

export type ScreenRect = {
  left: number;
  right: number;
  top: number;
  bottom: number;
};

export type LabelOcclusionInput = {
  id: number;
  bounds: ProjectedNodeBounds;
  viewSpaceDepth: number;
  labelRect: ScreenRect | undefined;
};

export type RenderedNode = {
  id: number;
  mesh: Object3D;
};

export function readRenderedNode(entity: Entity): RenderedNode {
  const mesh = entity.get(MeshRef);
  if (!mesh) throw new Error(`Rendered node ${entity.id()} has no mesh.`);
  return { id: entity.id(), mesh };
}

function toFSharpLabelOcclusionInput(input: LabelOcclusionInput): FSharpLabelOcclusionInput {
  const bounds = new FSharpProjectedNodeBounds(
    input.bounds.left,
    input.bounds.right,
    input.bounds.top,
    input.bounds.bottom
  );
  const labelRect =
    input.labelRect === undefined
      ? undefined
      : new FSharpScreenRect(
          input.labelRect.left,
          input.labelRect.right,
          input.labelRect.top,
          input.labelRect.bottom
        );
  return new FSharpLabelOcclusionInput(input.id, bounds, input.viewSpaceDepth, labelRect);
}

export function hideLabelsWithFSharp(inputs: readonly LabelOcclusionInput[]): readonly number[] {
  const generatedInputs = inputs.map(toFSharpLabelOcclusionInput);
  return Array.from(LabelPlacementModule_hiddenLabels(generatedInputs));
}

export function replaceLabelRegistration(
  previousCleanup: (() => void) | undefined,
  registry: LabelRegistry,
  id: LabelRegistryKey,
  mesh: Object3D | null,
  element: HTMLElement | null
): (() => void) | undefined {
  previousCleanup?.();
  return mesh && element ? registry.register(id, mesh, element) : undefined;
}

export interface LabelRegistry {
  readonly size: number;
  register(id: LabelRegistryKey, mesh: Object3D, element: HTMLElement): () => void;
  readSize(id: LabelRegistryKey): LabelSize | undefined;
  readElement(id: LabelRegistryKey): HTMLElement | undefined;
  readPlacement(
    mesh: Object3D,
    camera: Camera,
    canvasWidth: number,
    canvasHeight: number
  ): LabelPlacementLike | undefined;
  readByMesh(mesh: Object3D): RegisteredLabel | undefined;
  setPlacement(
    id: LabelRegistryKey,
    camera: Camera,
    canvasWidth: number,
    canvasHeight: number,
    placement: LabelPlacementLike
  ): void;
  setVisibility(id: LabelRegistryKey, visible: boolean): void;
  prune(ids: ReadonlySet<string>): void;
  has(id: LabelRegistryKey): boolean;
}

export type RegisteredLabel = {
  id: LabelRegistryKey;
  mesh: Object3D;
  element: HTMLElement;
  size: LabelSize | undefined;
};

export type LabelRegistryOptions = {
  createResizeObserver?: (
    callback: ResizeObserverCallback
  ) => Pick<ResizeObserver, "disconnect" | "observe"> | undefined;
  onSizeChanged?: (id: LabelRegistryKey, size: LabelSize) => void;
};

type PlacementCache = {
  camera: Camera;
  canvasWidth: number;
  canvasHeight: number;
  placement: LabelPlacementLike;
};

type Registration = RegisteredLabel & {
  observer: Pick<ResizeObserver, "disconnect"> | undefined;
  placement: PlacementCache | undefined;
};

function registryKey(id: LabelRegistryKey): string {
  return String(id);
}

function writeVisibility(element: HTMLElement, visible: boolean): void {
  const nextVisibility = visible ? "visible" : "hidden";
  if (element.style.visibility !== nextVisibility) {
    element.style.visibility = nextVisibility;
  }
}

function writePlacementTransform(element: HTMLElement, placement: LabelPlacementLike): void {
  const nextTransform = `translateX(-50%) scale(${placement.scale})`;
  if (element.style.transform !== nextTransform) {
    element.style.transform = nextTransform;
  }
}

function readResizeEntry(entries: readonly ResizeObserverEntry[]): LabelSize | undefined {
  const entry = entries[0];
  if (!entry) return undefined;
  const { width, height } = entry.contentRect;
  return Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0
    ? { width, height }
    : undefined;
}

export function createLabelRegistry(options: LabelRegistryOptions = {}): LabelRegistry {
  const registrations = new Map<string, Registration>();
  const meshKeys = new WeakMap<Object3D, string>();
  const createResizeObserver =
    options.createResizeObserver ??
    ((callback: ResizeObserverCallback) =>
      typeof ResizeObserver === "undefined" ? undefined : new ResizeObserver(callback));

  function register(id: LabelRegistryKey, mesh: Object3D, element: HTMLElement): () => void {
    const key = registryKey(id);
    const previous = registrations.get(key);
    previous?.observer?.disconnect();
    if (previous && meshKeys.get(previous.mesh) === key) {
      meshKeys.delete(previous.mesh);
    }

    const registration: Registration = {
      id,
      mesh,
      element,
      size: undefined,
      observer: undefined,
      placement: undefined,
    };
    registrations.set(key, registration);
    meshKeys.set(mesh, key);
    writeVisibility(element, false);
    if (element.dataset) {
      element.dataset.labelMeasured = "false";
    }

    const observer = createResizeObserver((entries) => {
      if (registrations.get(key) !== registration) return;
      const nextSize = readResizeEntry(entries);
      if (!nextSize) return;
      const previousSize = registration.size;
      if (previousSize?.width === nextSize.width && previousSize.height === nextSize.height) {
        return;
      }
      registration.size = nextSize;
      if (registration.element.dataset) {
        registration.element.dataset.labelMeasured = "true";
      }
      options.onSizeChanged?.(id, nextSize);
    });
    registration.observer = observer;
    observer?.observe(element);

    return () => {
      if (registrations.get(key) !== registration) return;
      registration.observer?.disconnect();
      registrations.delete(key);
      if (meshKeys.get(mesh) === key) meshKeys.delete(mesh);
    };
  }

  function readPlacement(
    mesh: Object3D,
    camera: Camera,
    canvasWidth: number,
    canvasHeight: number
  ): LabelPlacementLike | undefined {
    const key = meshKeys.get(mesh);
    const registration = key === undefined ? undefined : registrations.get(key);
    const cache = registration?.placement;
    return cache &&
      cache.camera === camera &&
      cache.canvasWidth === canvasWidth &&
      cache.canvasHeight === canvasHeight
      ? cache.placement
      : undefined;
  }

  return {
    get size() {
      return registrations.size;
    },
    register,
    readSize: (id) => registrations.get(registryKey(id))?.size,
    readElement: (id) => registrations.get(registryKey(id))?.element,
    readPlacement,
    readByMesh: (mesh) => {
      const key = meshKeys.get(mesh);
      const registration = key === undefined ? undefined : registrations.get(key);
      if (!registration) return undefined;
      return {
        id: registration.id,
        mesh: registration.mesh,
        element: registration.element,
        size: registration.size,
      };
    },
    setPlacement: (id, camera, canvasWidth, canvasHeight, placement) => {
      const registration = registrations.get(registryKey(id));
      if (!registration) return;
      registration.placement = { camera, canvasWidth, canvasHeight, placement };
      writePlacementTransform(registration.element, placement);
    },
    setVisibility: (id, visible) => {
      const registration = registrations.get(registryKey(id));
      if (registration) writeVisibility(registration.element, visible);
    },
    prune: (ids) => {
      for (const [key, registration] of registrations) {
        if (!ids.has(key)) {
          registration.observer?.disconnect();
          registrations.delete(key);
          if (meshKeys.get(registration.mesh) === key) meshKeys.delete(registration.mesh);
        }
      }
    },
    has: (id) => registrations.has(registryKey(id)),
  };
}

export type LabelOcclusionPassOptions = {
  registry: LabelRegistry;
  nodes: readonly RenderedNode[];
  camera: Camera;
  canvasWidth: number;
  canvasHeight: number;
  projectNodeBounds?: (node: RenderedNode) => LabelOcclusionInput["bounds"];
  readViewSpaceDepth?: (node: RenderedNode) => number;
  readPixelsPerWorldUnit?: (node: RenderedNode) => number;
  hideLabels: (inputs: readonly LabelOcclusionInput[]) => readonly LabelRegistryKey[];
};

export function runLabelOcclusionPass(options: LabelOcclusionPassOptions): void {
  const projectBounds =
    options.projectNodeBounds ??
    ((node: RenderedNode) =>
      projectNodeBounds(node.mesh, options.camera, options.canvasWidth, options.canvasHeight));
  const readDepth =
    options.readViewSpaceDepth ??
    ((node: RenderedNode) => readViewSpaceDepth(node.mesh, options.camera));
  const readPixels =
    options.readPixelsPerWorldUnit ??
    ((node: RenderedNode) =>
      readPixelsPerWorldUnit(node.mesh, options.camera, options.canvasHeight));
  const inputs: LabelOcclusionInput[] = [];
  const renderedIds = new Set<string>();

  for (const node of options.nodes) {
    const key = registryKey(node.id);
    renderedIds.add(key);
    const bounds = projectBounds(node);
    const viewSpaceDepth = readDepth(node);
    const registeredLabel = options.registry.readByMesh(node.mesh);
    let labelRect: ScreenRect | undefined;

    if (registeredLabel) {
      const placement = projectLabelPlacementFromBounds(bounds, readPixels(node));
      options.registry.setPlacement(
        registeredLabel.id,
        options.camera,
        options.canvasWidth,
        options.canvasHeight,
        placement
      );
      const size = registeredLabel.size;
      if (size) {
        labelRect = deriveLabelRect(placement, size);
      }
    }

    inputs.push({ id: node.id, bounds, viewSpaceDepth, labelRect });
  }

  const hiddenIds = new Set(options.hideLabels(inputs).map(registryKey));
  for (const node of options.nodes) {
    const registeredLabel = options.registry.readByMesh(node.mesh);
    if (!registeredLabel) continue;
    const measured = registeredLabel.size !== undefined;
    options.registry.setVisibility(
      registeredLabel.id,
      measured && !hiddenIds.has(registryKey(registeredLabel.id))
    );
  }
  options.registry.prune(renderedIds);
}

function deriveLabelRect(placement: LabelPlacementLike, size: LabelSize): ScreenRect {
  const width = size.width * placement.scale;
  const height = size.height * placement.scale;
  return {
    left: placement.x - width / 2,
    right: placement.x + width / 2,
    top: placement.y,
    bottom: placement.y + height,
  };
}

export const renderedLabelRegistry = createLabelRegistry();
