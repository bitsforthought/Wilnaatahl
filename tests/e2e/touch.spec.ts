import { expect, test, type Page } from "@playwright/test";
import type { CssPoint, ObserverSnapshot } from "../../src/testing/observer";
import { readObserver, waitForStableObserver, waitForNewObserverSession } from "./observer";
import {
  createTouchInput,
  installPointerEventProbe,
  readPointerEvents,
  type PointerEventRecord,
  type TouchContact,
  type TouchInput,
} from "./touch-input";

const familyFixture = "tests/e2e/fixtures/desktop-family.json";

async function importFamily(page: Page) {
  await page.goto("/");
  const previousSession = (await waitForStableObserver(page)).session;
  await page.locator('input[type="file"]').setInputFiles(familyFixture);
  await waitForNewObserverSession(page, previousSession);
}

function backgroundPoints(snapshot: ObserverSnapshot): TouchContact[] {
  const xRatios = [0.12, 0.88, 0.5, 0.25, 0.75];
  const yRatios = [0.88, 0.12, 0.5, 0.65, 0.35];
  const candidates = yRatios.flatMap((yRatio) =>
    xRatios.map((xRatio) => ({
      id: 0,
      x: snapshot.canvas.left + snapshot.canvas.width * xRatio,
      y: snapshot.canvas.top + snapshot.canvas.height * yRatio,
    }))
  );
  return candidates.filter((point) =>
    snapshot.nodes.every(
      (node) => Math.hypot(node.cssPosition.x - point.x, node.cssPosition.y - point.y) > 55
    )
  );
}

function findBackgroundPair(snapshot: ObserverSnapshot) {
  const candidates = backgroundPoints(snapshot);
  for (const first of candidates) {
    const second = candidates.find(
      (candidate) => Math.hypot(candidate.x - first.x, candidate.y - first.y) > 100
    );
    if (second) {
      return [
        { ...first, id: 1 },
        { ...second, id: 2 },
      ] as const;
    }
  }
  throw new Error("Could not find two touch points clear of rendered nodes.");
}

function cameraDistance(snapshot: ObserverSnapshot) {
  return Math.hypot(
    snapshot.camera.position.x - snapshot.camera.target.x,
    snapshot.camera.position.y - snapshot.camera.target.y,
    snapshot.camera.position.z - snapshot.camera.target.z
  );
}

async function readTouchTarget(page: Page, point: CssPoint) {
  return page.evaluate(({ x, y }) => {
    const element = document.elementFromPoint(x, y);
    return {
      tagName: element?.tagName,
      text: element?.textContent ?? "",
      pointerEvents: element ? getComputedStyle(element).pointerEvents : "none",
    };
  }, point);
}

function pointerEventsOfType(events: PointerEventRecord[], type: string) {
  return events.filter((event) => event.type === type);
}

async function moveContactsInSteps(
  touch: TouchInput,
  starting: readonly TouchContact[],
  ending: readonly TouchContact[],
  steps = 4
) {
  for (let step = 1; step <= steps; step += 1) {
    const progress = step / steps;
    await touch.moveContacts(
      ending.map((contact, index) => {
        const origin = starting[index];
        if (!origin) throw new Error(`Missing starting point for touch contact ${contact.id}.`);
        return {
          ...contact,
          x: origin.x + (contact.x - origin.x) * progress,
          y: origin.y + (contact.y - origin.y) * progress,
        };
      })
    );
  }
}

async function waitForTouchCameraToSettle(page: Page): Promise<ObserverSnapshot> {
  let previous = await readObserver(page);
  let stableFrames = 0;
  let latest = previous;

  await expect
    .poll(
      async () => {
        latest = await readObserver(page);
        const cameraPositionDelta = Math.hypot(
          latest.camera.position.x - previous.camera.position.x,
          latest.camera.position.y - previous.camera.position.y,
          latest.camera.position.z - previous.camera.position.z
        );
        const cameraTargetDelta = Math.hypot(
          latest.camera.target.x - previous.camera.target.x,
          latest.camera.target.y - previous.camera.target.y,
          latest.camera.target.z - previous.camera.target.z
        );
        stableFrames =
          latest.session === previous.session &&
          latest.frame > previous.frame &&
          !latest.dragging &&
          latest.nodes.length > 0 &&
          cameraPositionDelta < 0.001 &&
          cameraTargetDelta < 0.001
            ? stableFrames + 1
            : 0;
        previous = latest;
        return stableFrames;
      },
      { timeout: 10_000, intervals: [50, 100, 200] }
    )
    .toBeGreaterThanOrEqual(3);

  return latest;
}

async function dragBackgroundWithTouch(
  page: Page,
  id: number,
  start: TouchContact,
  delta: { x: number; y: number }
) {
  const touch = await createTouchInput(page);
  try {
    await touch.startContact({ ...start, id });
    await moveContactsInSteps(
      touch,
      [{ ...start, id }],
      [{ id, x: start.x + delta.x, y: start.y + delta.y }]
    );
    await touch.endGesture();
  } finally {
    await touch.close();
  }
}

test("[known defect pin] narrow-screen View-mode touch is intercepted by a node label", async ({
  page,
}) => {
  await importFamily(page);
  const snapshot = await waitForStableObserver(page);
  const node = snapshot.nodes.find(
    (candidate) =>
      candidate.personId === 103 &&
      candidate.cssPosition.x >= snapshot.canvas.left &&
      candidate.cssPosition.x <= snapshot.canvas.left + snapshot.canvas.width &&
      candidate.cssPosition.y >= snapshot.canvas.top &&
      candidate.cssPosition.y <= snapshot.canvas.top + snapshot.canvas.height
  );
  if (!node) throw new Error("Touch target person was not rendered.");

  const hitTarget = await readTouchTarget(page, node.cssPosition);
  await installPointerEventProbe(page);
  await page.touchscreen.tap(node.cssPosition.x, node.cssPosition.y);

  const touchEvents = await readPointerEvents(page);
  // The label's HTML wrapper wins hit-testing over the Three.js canvas at this viewport.
  expect(hitTarget.tagName).toBe("DIV");
  expect(hitTarget.text).toContain("Cora East");
  expect(hitTarget.pointerEvents).not.toBe("none");
  expect(touchEvents).toContainEqual(
    expect.objectContaining({
      type: "pointerdown",
      pointerType: "touch",
      isTrusted: true,
      targetTag: "DIV",
    })
  );
  expect(touchEvents).toContainEqual(
    expect.objectContaining({
      type: "pointerup",
      pointerType: "touch",
      isTrusted: true,
      targetTag: "DIV",
    })
  );
  const afterTap = await waitForStableObserver(page);
  expect(afterTap.selectedPersonIds).toEqual([]);
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("[known defect pin] Move-mode touch on a projected node leaves it unselected", async ({
  page,
}) => {
  await importFamily(page);
  await page.getByRole("button", { name: "Move" }).tap();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();

  const snapshot = await waitForStableObserver(page);
  const node = snapshot.nodes.find(
    (candidate) =>
      candidate.personId === 103 &&
      candidate.cssPosition.x >= snapshot.canvas.left &&
      candidate.cssPosition.x <= snapshot.canvas.left + snapshot.canvas.width &&
      candidate.cssPosition.y >= snapshot.canvas.top &&
      candidate.cssPosition.y <= snapshot.canvas.top + snapshot.canvas.height
  );
  if (!node) throw new Error("Touch target person was not rendered.");

  const hitTarget = await readTouchTarget(page, node.cssPosition);
  await installPointerEventProbe(page);
  await page.touchscreen.tap(node.cssPosition.x, node.cssPosition.y);

  // Diagnostic pin for the existing projected-click interception defect.
  const events = await readPointerEvents(page);
  expect(hitTarget.tagName).toBe("DIV");
  expect(hitTarget.text).toContain("Cora East");
  expect(events).toContainEqual(
    expect.objectContaining({
      type: "pointerdown",
      pointerType: "touch",
      isTrusted: true,
      targetTag: "DIV",
    })
  );
  const afterTap = await waitForStableObserver(page);
  expect(afterTap.selectedPersonIds).toEqual([]);
});

test("one-finger background drag rotates the camera without moving nodes", async ({ page }) => {
  await importFamily(page);
  const before = await waitForStableObserver(page);
  const point = backgroundPoints(before)[0];
  if (!point) throw new Error("Could not find a touch point clear of rendered nodes.");

  await dragBackgroundWithTouch(page, 1, point, { x: 55, y: 35 });
  const after = await waitForTouchCameraToSettle(page);
  const beforeDirection = {
    x: before.camera.position.x - before.camera.target.x,
    y: before.camera.position.y - before.camera.target.y,
    z: before.camera.position.z - before.camera.target.z,
  };
  const afterDirection = {
    x: after.camera.position.x - after.camera.target.x,
    y: after.camera.position.y - after.camera.target.y,
    z: after.camera.position.z - after.camera.target.z,
  };
  const directionSimilarity =
    (beforeDirection.x * afterDirection.x +
      beforeDirection.y * afterDirection.y +
      beforeDirection.z * afterDirection.z) /
    (cameraDistance(before) * cameraDistance(after));

  expect(after.camera.position).not.toEqual(before.camera.position);
  expect(after.camera.target).toEqual(before.camera.target);
  expect(cameraDistance(after)).toBeCloseTo(cameraDistance(before), 2);
  expect(directionSimilarity).toBeLessThan(0.9999);
  expect(after.nodes.map((node) => [node.personId, node.logicalPosition])).toEqual(
    before.nodes.map((node) => [node.personId, node.logicalPosition])
  );
});

test("pinch gestures zoom without changing logical node positions", async ({ page }) => {
  await importFamily(page);
  const before = await waitForStableObserver(page);
  const [first, second] = findBackgroundPair(before);
  const middle = { x: (first.x + second.x) / 2, y: (first.y + second.y) / 2 };
  const touch = await createTouchInput(page);

  try {
    await touch.startContact(first);
    await touch.startContact(second);
    const spread = [
      {
        id: first.id,
        x: middle.x + (first.x - middle.x) * 1.2,
        y: middle.y + (first.y - middle.y) * 1.2,
      },
      {
        id: second.id,
        x: middle.x + (second.x - middle.x) * 1.2,
        y: middle.y + (second.y - middle.y) * 1.2,
      },
    ];
    await moveContactsInSteps(touch, [first, second], spread);
    await touch.endGesture();
  } finally {
    await touch.close();
  }

  const zoomedIn = await waitForTouchCameraToSettle(page);
  expect(cameraDistance(zoomedIn)).toBeLessThan(cameraDistance(before));

  const [pinchFirst, pinchSecond] = findBackgroundPair(zoomedIn);
  const pinchMiddle = {
    x: (pinchFirst.x + pinchSecond.x) / 2,
    y: (pinchFirst.y + pinchSecond.y) / 2,
  };
  const pinch = await createTouchInput(page);
  try {
    await pinch.startContact(pinchFirst);
    await pinch.startContact(pinchSecond);
    const together = [
      {
        id: pinchFirst.id,
        x: pinchMiddle.x + (pinchFirst.x - pinchMiddle.x) * 0.8,
        y: pinchMiddle.y + (pinchFirst.y - pinchMiddle.y) * 0.8,
      },
      {
        id: pinchSecond.id,
        x: pinchMiddle.x + (pinchSecond.x - pinchMiddle.x) * 0.8,
        y: pinchMiddle.y + (pinchSecond.y - pinchMiddle.y) * 0.8,
      },
    ];
    await moveContactsInSteps(pinch, [pinchFirst, pinchSecond], together);
    await pinch.endGesture();
  } finally {
    await pinch.close();
  }

  const zoomedOut = await waitForTouchCameraToSettle(page);
  expect(cameraDistance(zoomedOut)).toBeGreaterThan(cameraDistance(zoomedIn));
  expect(zoomedOut.nodes.map((node) => [node.personId, node.logicalPosition])).toEqual(
    before.nodes.map((node) => [node.personId, node.logicalPosition])
  );
});

test("two-finger pan and contact cancellation leave later touch input usable", async ({ page }) => {
  await importFamily(page);
  const before = await waitForStableObserver(page);
  const [first, second] = findBackgroundPair(before);
  await installPointerEventProbe(page);
  const panTouch = await createTouchInput(page);

  try {
    await panTouch.startContact(first);
    await panTouch.startContact(second);
    const moved = [
      { ...first, x: first.x + 25, y: first.y + 20 },
      { ...second, x: second.x + 25, y: second.y + 20 },
    ];
    await moveContactsInSteps(panTouch, [first, second], moved);
    const pointerDowns = pointerEventsOfType(await readPointerEvents(page), "pointerdown");
    expect(pointerDowns).toHaveLength(2);
    expect(new Set(pointerDowns.map((event) => event.pointerId)).size).toBe(2);
    expect(pointerDowns.every((event) => event.pointerType === "touch" && event.isTrusted)).toBe(
      true
    );
    await panTouch.endGesture();
  } finally {
    await panTouch.close();
  }

  const panned = await waitForTouchCameraToSettle(page);
  expect(panned.camera.target).not.toEqual(before.camera.target);

  const [cancelPoint] = backgroundPoints(panned);
  if (!cancelPoint) throw new Error("Could not find a point for touch cancellation.");
  const cancelled = await createTouchInput(page);
  const beforeCancel = await readPointerEvents(page);
  try {
    await cancelled.startContact({ ...cancelPoint, id: 3 });
    await moveContactsInSteps(
      cancelled,
      [{ ...cancelPoint, id: 3 }],
      [{ id: 3, x: cancelPoint.x + 24, y: cancelPoint.y + 18 }]
    );
    await cancelled.cancel();
  } finally {
    await cancelled.close();
  }
  const afterCancel = await readPointerEvents(page);
  const cancelPointerDowns = pointerEventsOfType(afterCancel, "pointerdown").slice(
    pointerEventsOfType(beforeCancel, "pointerdown").length
  );
  const cancelPointerId = cancelPointerDowns[cancelPointerDowns.length - 1]?.pointerId;
  if (cancelPointerId === undefined) throw new Error("Cancellation did not start a touch pointer.");
  expect(
    afterCancel.some(
      (event) =>
        event.type === "pointercancel" &&
        event.pointerId === cancelPointerId &&
        event.pointerType === "touch" &&
        event.isTrusted
    )
  ).toBe(true);

  const beforeRecovery = await waitForTouchCameraToSettle(page);
  await dragBackgroundWithTouch(page, 4, cancelPoint, { x: 40, y: -25 });
  const afterRecovery = await waitForTouchCameraToSettle(page);

  expect(afterRecovery.camera.position).not.toEqual(beforeRecovery.camera.position);
});
