import { describe, expect, it } from "vitest";
import {
  BoxGeometry,
  Camera,
  Mesh,
  MeshBasicMaterial,
  OrthographicCamera,
  PerspectiveCamera,
} from "three";
import {
  calculateLabelPosition,
  projectLabelPlacement,
} from "../../../src/react-components/labelPlacement";

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

describe("projectLabelPlacement", () => {
  it("places an orthographic label below the projected node", () => {
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.zoom = 2;
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const placement = projectLabelPlacement(mesh, camera, canvasWidth, canvasHeight);

    expect(placement.X).toBeCloseTo(400);
    expect(placement.Y).toBeCloseTo(428);
    expect(placement.Scale).toBeCloseTo(1.5);
  });

  it("uses rotated perspective view-space depth for scale", () => {
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    mesh.position.set(1, 0, 0);
    const camera = new PerspectiveCamera(
      verticalFieldOfViewDegrees,
      aspectRatio,
      cameraNear,
      cameraFar
    );
    camera.position.set(4, 3, 9);
    camera.lookAt(0, 0, 0);
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    const placement = projectLabelPlacement(mesh, camera, canvasWidth, canvasHeight);
    expect(placement.X).toBeCloseTo(467.869, 2);
    expect(placement.Y).toBeCloseTo(402.131, 2);
    expect(placement.Scale).toBeCloseTo(0.7271221135, 8);
  });

  it("uses the default scale for an unsupported camera type", () => {
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const placement = projectLabelPlacement(mesh, new Camera(), canvasWidth, canvasHeight);

    expect(placement.Scale).toBe(0.0125);
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
    const atCameraPlane = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const justInFrontOfCameraPlane = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    justInFrontOfCameraPlane.position.z = -Number.EPSILON;
    const behindCameraPlane = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    behindCameraPlane.position.z = Number.EPSILON;

    const atCameraPlanePlacement = projectLabelPlacement(
      atCameraPlane,
      camera,
      canvasWidth,
      canvasHeight
    );
    const justInFrontPlacement = projectLabelPlacement(
      justInFrontOfCameraPlane,
      camera,
      canvasWidth,
      canvasHeight
    );
    const behindCameraPlacement = projectLabelPlacement(
      behindCameraPlane,
      camera,
      canvasWidth,
      canvasHeight
    );

    expect(Number.isFinite(atCameraPlanePlacement.Scale)).toBe(true);
    expect(atCameraPlanePlacement.Scale).toBe(justInFrontPlacement.Scale);
    expect(atCameraPlanePlacement.Scale).toBe(behindCameraPlacement.Scale);
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
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.position.z = 5;
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
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();

    expect(
      calculateLabelPosition(mesh, camera, { width: canvasWidth, height: canvasHeight }, null)
    ).toEqual([400, 368]);
  });

  it("updates the label scale when orthographic zoom changes", () => {
    const mesh = new Mesh(new BoxGeometry(2, 2, 2), new MeshBasicMaterial());
    const camera = new OrthographicCamera(
      cameraLeft,
      cameraRight,
      cameraTop,
      cameraBottom,
      cameraNear,
      cameraFar
    );
    camera.position.z = 5;
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const labelElement = { style: { transform: "", visibility: "" } } as HTMLElement;

    calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );
    camera.zoom = 2;
    camera.updateProjectionMatrix();
    calculateLabelPosition(
      mesh,
      camera,
      { width: canvasWidth, height: canvasHeight },
      labelElement
    );

    expect(labelElement.style.transform).toBe("translateX(-50%) scale(1.5)");
  });
});
