import type { Camera } from "three";
import type { World } from "koota";
import { CanvasDimensions } from "../generated/ViewModel/LabelPlacement";
import { CameraRef, WorldCanvasDimensions } from "./traits";

function syncCameraReference(world: World, camera: Camera): void {
  const cameraEntity = world.query(CameraRef)[0];
  if (cameraEntity === undefined) {
    world.spawn(CameraRef(camera));
    return;
  }

  if (cameraEntity.get(CameraRef) !== camera) {
    cameraEntity.set(CameraRef, camera);
  }
}

function syncCanvasDimensions(world: World, width: number, height: number): void {
  const dimensions = new CanvasDimensions(width, height);
  if (!world.has(WorldCanvasDimensions)) {
    world.add(WorldCanvasDimensions(dimensions));
    return;
  }

  const currentDimensions = world.get(WorldCanvasDimensions);
  if (
    currentDimensions?.Width !== dimensions.Width ||
    currentDimensions?.Height !== dimensions.Height
  ) {
    world.set(WorldCanvasDimensions, dimensions);
  }
}

export function syncRenderingContext(
  world: World,
  camera: Camera,
  width: number,
  height: number
): void {
  syncCameraReference(world, camera);
  syncCanvasDimensions(world, width, height);
}
