import { describe, expect, it, vi } from "vitest";
import {
  BoxGeometry,
  Camera,
  Mesh,
  MeshBasicMaterial,
  OrthographicCamera,
  PerspectiveCamera,
  Object3D,
  Vector3,
} from "three";
import { calculateLabelPosition } from "../../../src/react-components/labelPlacement";

const canvasWidth = 800;
const canvasHeight = 600;
const cameraLeft = -5;
const cameraRight = 5;
const cameraTop = 5;
const cameraBottom = -5;
const verticalFieldOfViewDegrees = 55;
const aspectRatio = 1;
const cameraNear = 0.1;
const cameraFar = 100;
const nodeSideLength = 2;
// This distance keeps the entire origin-centred cube in front of the camera
// and between its clipping planes; it is a fixture choice, not a product constraint.
const orthographicCameraDistance = 5;
const doubledCameraZoom = 2;

function readScale(labelElement: HTMLElement): number {
  const match = /scale\(([^)]+)\)$/.exec(labelElement.style.transform);
  if (!match) throw new Error(`Label transform has no scale: ${labelElement.style.transform}`);
  return Number(match[1]);
}

describe("calculateLabelPosition", () => {
  it("places an orthographic label below the projected node", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.zoom = doubledCameraZoom;
    camera.position.z = orthographicCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;
    const placement = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(placement).toEqual([400, 428]);
    expect(readScale(labelElement)).toBeCloseTo(1.5);
  });

  it("uses rotated perspective view-space depth for scale", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const meshOffsetX = 1;
    mesh.position.set(meshOffsetX, 0, 0);
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    const cameraPosition = { x: 4, y: 3, z: 9 };
    camera.position.set(cameraPosition.x, cameraPosition.y, cameraPosition.z);
    camera.lookAt(0, 0, 0);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;
    const placement = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );
    expect(placement[0]).toBeCloseTo(467.869, 2);
    expect(placement[1]).toBeCloseTo(402.131, 2);
    expect(readScale(labelElement)).toBeCloseTo(0.7271221135, 8);
  });

  it("uses the default scale for an unsupported camera type", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;
    calculateLabelPosition(
      mesh,
      new Camera(),
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(readScale(labelElement)).toBe(0.0125);
  });

  it("clamps perspective depth at zero, near zero, and behind the camera", () => {
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const atCameraPlane = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const justInFrontOfCameraPlane = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const justInFrontDepth = -Number.EPSILON;
    justInFrontOfCameraPlane.position.z = justInFrontDepth;
    const behindCameraPlane = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const behindCameraDepth = Number.EPSILON;
    behindCameraPlane.position.z = behindCameraDepth;

    const atCameraPlaneLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    const justInFrontLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    const behindCameraLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    calculateLabelPosition(
      atCameraPlane,
      camera,
      { width: canvasWidth, height: canvasHeight },
      atCameraPlaneLabel
    );
    calculateLabelPosition(
      justInFrontOfCameraPlane,
      camera,
      { width: canvasWidth, height: canvasHeight },
      justInFrontLabel
    );
    calculateLabelPosition(
      behindCameraPlane,
      camera,
      { width: canvasWidth, height: canvasHeight },
      behindCameraLabel
    );

    expect(Number.isFinite(readScale(atCameraPlaneLabel))).toBe(true);
    expect(readScale(atCameraPlaneLabel)).toBe(readScale(justInFrontLabel));
    expect(readScale(atCameraPlaneLabel)).toBe(readScale(behindCameraLabel));
  });

  it("returns the origin when the mesh reference is unavailable", () => {
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );

    expect(
      calculateLabelPosition(null, camera, { width: canvasWidth, height: canvasHeight }, null)
    ).toEqual([0, 0]);
  });

  it("updates the label transform while returning the projected anchor", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.position.z = orthographicCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;

    const position = calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(position).toEqual([400, 368]);
    expect(labelElement.style.transform).toBe("translateX(-50%) scale(0.75)");
    expect(labelElement.style.visibility).toBe("visible");
  });

  it("returns the projected anchor without a mounted label element", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.position.z = orthographicCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    expect(
      calculateLabelPosition(mesh, camera, { width: canvasWidth, height: canvasHeight }, null)
    ).toEqual([400, 368]);
  });

  it("updates the label scale when orthographic zoom changes", () => {
    const mesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.position.z = orthographicCameraDistance;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;

    calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );
    camera.zoom = doubledCameraZoom;
    camera.updateProjectionMatrix();
    calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(labelElement.style.transform).toBe("translateX(-50%) scale(1.5)");
  });

  it("keeps the outer scale intact when scale calculation is re-entered", () => {
    const outerMesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const outerMeshDepth = -8;
    outerMesh.position.z = outerMeshDepth;
    const nestedMesh = new Mesh(
      new BoxGeometry(nodeSideLength, nodeSideLength, nodeSideLength),
      new MeshBasicMaterial()
    );
    const nestedMeshDepth = -2;
    nestedMesh.position.z = nestedMeshDepth;
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const htmlSize = { width: canvasWidth, height: canvasHeight };
    const expectedLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
    const expectedPosition = calculateLabelPosition(outerMesh, camera, htmlSize, expectedLabel);

    const originalGetWorldPosition = Object.getOwnPropertyDescriptor(
      Object3D.prototype,
      "getWorldPosition"
    )!.value as (this: Object3D, target: Vector3) => Vector3;
    let nested = false;
    const positionSpy = vi
      .spyOn(Object3D.prototype, "getWorldPosition")
      .mockImplementation(function (this: Object3D, target: Vector3) {
        const position = originalGetWorldPosition.call(this, target);
        if (!nested) {
          nested = true;
          calculateLabelPosition(nestedMesh, camera, htmlSize, null);
        }
        return position;
      });

    try {
      const actualLabel = { style: { transform: "", visibility: "" } } as HTMLElement;
      expect(calculateLabelPosition(outerMesh, camera, htmlSize, actualLabel)).toEqual(
        expectedPosition
      );
      expect(actualLabel.style.transform).toBe(expectedLabel.style.transform);
    } finally {
      positionSpy.mockRestore();
    }
  });
});
