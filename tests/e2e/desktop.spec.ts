import { expect, test, type Page } from "@playwright/test";
import {
  clickNode,
  readMinimalLabelScene,
  readObserver,
  waitForFrameAfter,
  waitForStableObserver,
  type LabelObservation,
  type LabelRect,
  type MinimalLabelScene,
} from "./observer";
import { importFixture } from "./test-utils";
import type { ObserverNode, ObserverSnapshot } from "../../src/testing/observer";

const familyFixture = "tests/e2e/fixtures/desktop-family.json";
const warningFixture = "tests/e2e/fixtures/desktop-warning.json";
const invalidFixture = "tests/e2e/fixtures/desktop-invalid.json";

declare global {
  var __WILNAATAHL_LABEL_LIFECYCLE_PROBE__:
    | {
        stop(): {
          remounted: boolean;
          mountedVisibility: string | null;
          firstVisible: LabelRect | null;
          settled: LabelRect | null;
        };
      }
    | undefined;
}

function labelsByPerson(scene: MinimalLabelScene): ReadonlyMap<number, LabelObservation> {
  return new Map(scene.labels.map((label) => [label.personId, label]));
}

function measuredLabel(scene: MinimalLabelScene, personId: number): LabelObservation | undefined {
  const label = scene.labels.find((candidate) => candidate.personId === personId);
  return label && label.rect.width > 0 && label.rect.height > 0 ? label : undefined;
}

async function waitForMeasuredLabels(
  page: Page,
  personIds: readonly number[]
): Promise<MinimalLabelScene> {
  let latest: MinimalLabelScene | undefined;
  await expect
    .poll(
      async () => {
        try {
          const scene = await readMinimalLabelScene(page);
          latest = scene;
          return personIds.every((personId) => {
            const label = measuredLabel(scene, personId);
            return label?.visible === true;
          });
        } catch {
          return false;
        }
      },
      { timeout: 5_000, intervals: [50, 100, 200] }
    )
    .toBe(true);
  if (!latest) throw new Error("Measured labels were not observed.");
  return latest;
}

async function waitForMountedMeasuredLabels(
  page: Page,
  personIds: readonly number[]
): Promise<MinimalLabelScene> {
  let latest: MinimalLabelScene | undefined;
  await expect
    .poll(
      async () => {
        try {
          const scene = await readMinimalLabelScene(page);
          latest = scene;
          return personIds.every((personId) => measuredLabel(scene, personId) !== undefined);
        } catch {
          return false;
        }
      },
      { timeout: 5_000, intervals: [50, 100, 200] }
    )
    .toBe(true);
  if (!latest) throw new Error("Mounted labels were not measured.");
  return latest;
}

function visiblePersonIds(scene: MinimalLabelScene): number[] {
  return scene.labels
    .filter((label) => label.visible)
    .map((label) => label.personId)
    .sort();
}

function expectLabelsBelowNodes(scene: MinimalLabelScene) {
  const { snapshot } = scene;
  const labels = labelsByPerson(scene);
  const labelGapPixels = 8;
  const geometryRoundingTolerancePixels = 1;
  for (const node of snapshot.nodes) {
    const bounds = node.projectedBounds;
    const label = labels.get(node.personId)?.rect;
    expect(bounds).not.toBeNull();
    expect(label).not.toBeNull();
    if (!bounds || !label) continue;

    const canvas = snapshot.canvas;
    const nodeBottom = canvas.top + bounds.bottom;
    const nodeCenter = canvas.left + (bounds.left + bounds.right) / 2;
    const labelCenter = label.left + label.width / 2;
    const geometry = JSON.stringify({ personId: node.personId, bounds, label, nodeBottom });
    expect(label.width, geometry).toBeGreaterThan(0);
    expect(label.height, geometry).toBeGreaterThan(0);
    expect(label.top - nodeBottom, geometry).toBeCloseTo(labelGapPixels, 0);
    expect(Math.abs(labelCenter - nodeCenter), geometry).toBeLessThanOrEqual(
      geometryRoundingTolerancePixels
    );
  }
}

function expectLabelRectsToMatch(actual: LabelRect, expected: LabelRect, description: string) {
  const tolerancePixels = 1;
  expect(Math.abs(actual.width - expected.width), description).toBeLessThanOrEqual(tolerancePixels);
  expect(Math.abs(actual.height - expected.height), description).toBeLessThanOrEqual(
    tolerancePixels
  );
  expect(Math.abs(actual.left - expected.left), description).toBeLessThanOrEqual(tolerancePixels);
  expect(Math.abs(actual.top - expected.top), description).toBeLessThanOrEqual(tolerancePixels);
}

interface OcclusionEvidence {
  target: ObserverNode;
  nearer: ObserverNode;
}

function findOcclusionEvidence(scene: MinimalLabelScene): OcclusionEvidence | undefined {
  const { snapshot } = scene;
  const labels = labelsByPerson(scene);
  for (const target of snapshot.nodes) {
    const targetLabel = labels.get(target.personId);
    if (!targetLabel || targetLabel.visible) continue;
    for (const nearer of snapshot.nodes) {
      const nearerLabel = labels.get(nearer.personId);
      if (
        nearer.personId === target.personId ||
        !nearerLabel?.visible ||
        nearer.viewSpaceDepth >= target.viewSpaceDepth
      ) {
        continue;
      }
      const bounds = nearer.projectedBounds;
      if (!bounds) continue;
      const nearerBounds = {
        left: snapshot.canvas.left + bounds.left,
        right: snapshot.canvas.left + bounds.right,
        top: snapshot.canvas.top + bounds.top,
        bottom: snapshot.canvas.top + bounds.bottom,
      };
      const overlaps =
        targetLabel.rect.left < nearerBounds.right &&
        nearerBounds.left < targetLabel.rect.left + targetLabel.rect.width &&
        targetLabel.rect.top < nearerBounds.bottom &&
        nearerBounds.top < targetLabel.rect.top + targetLabel.rect.height;
      if (overlaps) return { target, nearer };
    }
  }
  return undefined;
}

async function waitForVisiblePeople(
  page: Page,
  expectedPersonIds: readonly number[]
): Promise<MinimalLabelScene> {
  let latest: MinimalLabelScene | undefined;
  await expect
    .poll(
      async () => {
        latest = await readMinimalLabelScene(page);
        return visiblePersonIds(latest).join(",");
      },
      { timeout: 5_000, intervals: [50, 100, 200] }
    )
    .toBe(expectedPersonIds.join(","));
  if (!latest) throw new Error("Visible labels were not observed.");
  return latest;
}

async function orbitBy(page: Page, snapshot: ObserverSnapshot, delta: { x: number; y: number }) {
  const start = {
    x: snapshot.canvas.left + snapshot.canvas.width / 2,
    y: snapshot.canvas.top + snapshot.canvas.height / 2,
  };
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(start.x + delta.x, start.y + delta.y, { steps: 8 });
  await page.mouse.up();
}

async function startLabelLifecycleProbe(page: Page, personId: number) {
  await page.evaluate((id) => {
    const selector = `.node-label[data-person-id="${id}"]`;
    const initial = document.querySelector<HTMLElement>(selector);
    if (!initial) throw new Error(`Could not find label for person ${id}.`);

    const readRect = (element: HTMLElement): LabelRect => {
      const rect = element.getBoundingClientRect();
      return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
    };
    let firstVisible: LabelRect | null = null;
    let mountedVisibility: string | null = null;
    let remounted = false;
    let frame = 0;
    const mutationObserver = new MutationObserver(() => {
      const current = document.querySelector<HTMLElement>(selector);
      if (current && current !== initial && mountedVisibility === null) {
        mountedVisibility = getComputedStyle(current).visibility;
      }
    });
    mutationObserver.observe(document.body, { childList: true, subtree: true });

    const sample = () => {
      const current = document.querySelector<HTMLElement>(selector);
      if (current && current !== initial) {
        remounted = true;
        if (!firstVisible && getComputedStyle(current).visibility !== "hidden") {
          firstVisible = readRect(current);
        }
      }
      // Schedule the next browser frame asynchronously so sampling cannot grow the call stack.
      frame = requestAnimationFrame(sample);
    };

    frame = requestAnimationFrame(sample);
    globalThis.__WILNAATAHL_LABEL_LIFECYCLE_PROBE__ = {
      stop() {
        cancelAnimationFrame(frame);
        mutationObserver.disconnect();
        const current = document.querySelector<HTMLElement>(selector);
        return {
          remounted,
          mountedVisibility,
          firstVisible,
          settled: current ? readRect(current) : null,
        };
      },
    };
  }, personId);
}

async function readLabelLifecycleProbe(page: Page) {
  return page.evaluate(() => {
    const probe = globalThis.__WILNAATAHL_LABEL_LIFECYCLE_PROBE__;
    if (!probe) throw new Error("Label lifecycle probe is not installed.");
    const result = probe.stop();
    delete globalThis.__WILNAATAHL_LABEL_LIFECYCLE_PROBE__;
    return result;
  });
}

test("initial import renders every visible node label before interaction", async ({ page }) => {
  await page.goto("/");
  await waitForStableObserver(page);
  await importFixture(page, familyFixture);

  const scene = await waitForMeasuredLabels(
    page,
    (await waitForStableObserver(page, { requireProjectedBounds: true })).nodes.map(
      (node) => node.personId
    )
  );
  const snapshot = scene.snapshot;
  expect(snapshot.nodes.map((node) => node.personId).sort()).toEqual([101, 102, 103]);
  expectLabelsBelowNodes(scene);
  await expect(page.getByText("Ada's title")).toBeVisible();
  await expect(page.getByText("Ben's title")).toBeVisible();
  await expect(page.getByText("Cora's title")).toBeVisible();

  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.getByRole("button", { name: "Dismiss detail" }).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(page.getByText("Ada's title")).toBeVisible();
  await expect(page.getByText("Ben's title")).toBeVisible();
  await expect(page.getByText("Cora's title")).toBeVisible();
  const afterDismiss = await waitForStableObserver(page, { requireProjectedBounds: true });
  expect(afterDismiss.camera).toEqual(snapshot.camera);
  expect(afterDismiss.nodes).toEqual(snapshot.nodes);
});

test("orbited labels remain below their projected node bounds", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);

  const before = await waitForStableObserver(page);
  const orbitStart = {
    x: before.canvas.left + before.canvas.width / 2,
    y: before.canvas.top + before.canvas.height / 2,
  };
  await page.mouse.move(orbitStart.x, orbitStart.y);
  await page.mouse.down();
  await page.mouse.move(orbitStart.x + 120, orbitStart.y + 35, { steps: 6 });
  await page.mouse.up();

  const stable = await waitForStableObserver(page, {
    requireRenderedState: false,
    requireProjectedBounds: true,
  });
  await waitForFrameAfter(page, stable);
  const after = await waitForMountedMeasuredLabels(
    page,
    stable.nodes.map((node) => node.personId)
  );
  expect(after.snapshot.camera.position).not.toEqual(before.camera.position);
  expectLabelsBelowNodes(after);
});

test("orbiting can occlude a farther label and reversing the orbit restores it", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);

  const initial = await waitForMeasuredLabels(
    page,
    (await waitForStableObserver(page, { requireProjectedBounds: true })).nodes.map(
      (node) => node.personId
    )
  );
  const initiallyVisible = visiblePersonIds(initial);
  expect(initiallyVisible.length).toBeGreaterThan(1);

  const orbitDeltas = [
    { x: 180, y: 0 },
    { x: 180, y: 30 },
    { x: 180, y: -30 },
    { x: 180, y: 30 },
    { x: 180, y: -30 },
    { x: 180, y: 30 },
  ];
  const appliedDeltas: { x: number; y: number }[] = [];
  let latest = initial;
  let evidence: OcclusionEvidence | undefined;
  for (const delta of orbitDeltas) {
    await orbitBy(page, latest.snapshot, delta);
    const stable = await waitForStableObserver(page, {
      requireRenderedState: false,
      requireProjectedBounds: true,
    });
    appliedDeltas.push(delta);
    latest = await waitForMountedMeasuredLabels(
      page,
      stable.nodes.map((node) => node.personId)
    );
    evidence = findOcclusionEvidence(latest);
    if (evidence) break;
  }

  expect(evidence).toBeDefined();
  if (!evidence) throw new Error("Expected an occlusion evidence pair.");

  for (const delta of appliedDeltas.reverse()) {
    await orbitBy(page, latest.snapshot, { x: -delta.x, y: -delta.y });
    const stable = await waitForStableObserver(page, {
      requireRenderedState: false,
      requireProjectedBounds: true,
    });
    latest = await waitForMountedMeasuredLabels(
      page,
      stable.nodes.map((node) => node.personId)
    );
  }

  await waitForVisiblePeople(page, initiallyVisible);
});

test("node labels declare non-selectability and stay unselectable during gestures", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);

  const label = page.getByText("Ada's title");
  await expect(label).toBeVisible();
  // Pointer-transparent labels cannot produce DOM selection by gesture alone, so the computed
  // style assertion distinguishes missing user-select from the existing pointer passthrough.
  const labelStyles = await label.evaluate((element) => {
    const styles = getComputedStyle(element);
    return {
      color: styles.color,
      textAlign: styles.textAlign,
      whiteSpace: styles.whiteSpace,
      pointerEvents: styles.pointerEvents,
      userSelect: styles.userSelect,
      webkitUserSelect: styles.webkitUserSelect,
    };
  });
  expect(labelStyles).toEqual({
    color: "rgb(255, 255, 255)",
    textAlign: "center",
    whiteSpace: "pre-line",
    pointerEvents: "none",
    userSelect: "none",
    webkitUserSelect: "none",
  });

  const labelBox = await label.boundingBox();
  if (!labelBox) throw new Error("Node label did not have a layout box.");
  const labelPoint = {
    x: labelBox.x + labelBox.width / 2,
    y: labelBox.y + labelBox.height / 2,
  };

  await page.mouse.click(labelPoint.x, labelPoint.y, { clickCount: 3 });
  expect(await page.evaluate(() => window.getSelection()?.toString() ?? "")).toBe("");

  await page.mouse.move(labelBox.x + 2, labelPoint.y);
  await page.mouse.down();
  await page.mouse.move(labelBox.x + labelBox.width - 2, labelPoint.y, { steps: 4 });
  await page.mouse.up();
  expect(await page.evaluate(() => window.getSelection()?.toString() ?? "")).toBe("");
});

test("view-mode projected node input opens and dismisses the matching detail card", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);

  // This fixture does not exercise detail placement at viewport edges.
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toContainText("Ada North");
  await expect(page.getByRole("dialog")).toBeVisible();

  await page.getByRole("button", { name: "Dismiss detail" }).click();
  await expect(page.getByRole("dialog")).toBeHidden();
  expect((await readObserver(page)).selectedPersonIds).toEqual([]);
});

test("detail overlay isolates card input and suppresses camera wheel gestures", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toContainText("Ada North");

  const before = await readObserver(page);
  await page.getByRole("dialog").getByText("Ada North").click();
  await page.mouse.move(before.canvas.left + 24, before.canvas.top + 24);
  await page.mouse.wheel(0, 500);
  const after = await waitForStableObserver(page);

  expect(after.camera).toEqual(before.camera);
  expect(after.selectedPersonIds).toEqual([101]);
  await expect(page.getByRole("dialog")).toBeVisible();
});

test("View and Move controls clear selection and expose mode-specific history controls", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);

  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);
  await expect(page.getByRole("button", { name: "Undo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Redo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Multi-select" })).toBeVisible();

  await page.getByRole("button", { name: "View" }).click();
  await expect(page.getByRole("button", { name: "Move" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo" })).toBeHidden();
  await expect(page.getByRole("button", { name: "Redo" })).toBeHidden();
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);
});

test("Move-mode projected clicks select multiple nodes through their labels", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toBeVisible();

  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([]);

  await page.getByRole("button", { name: "Multi-select" }).click();
  await expect(page.getByRole("button", { name: "Single-select" })).toBeVisible();
  const projectedNode = (await readObserver(page)).nodes.find(
    (candidate) => candidate.personId === 102
  );
  if (!projectedNode) throw new Error("Projected fixture node was not rendered.");
  const hitTarget = await page.evaluate(({ x, y }) => {
    const element = document.elementFromPoint(x, y);
    return element?.tagName;
  }, projectedNode.cssPosition);
  expect(hitTarget).toBe("CANVAS");
  await clickNode(page, 102);
  await clickNode(page, 103);
  expect((await waitForStableObserver(page)).selectedPersonIds).toEqual([102, 103]);
});

test("Move-mode label remounts preserve first-visible and settled geometry", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();

  const before = await waitForStableObserver(page, { requireProjectedBounds: true });
  const beforeNode = before.nodes.find((node) => node.personId === 102);
  const beforeScene = await waitForMeasuredLabels(page, [102]);
  const beforeLabel = beforeScene.labels.find((label) => label.personId === 102)?.rect;
  if (!beforeNode || !beforeLabel)
    throw new Error("Projected label was not rendered before selection.");

  await startLabelLifecycleProbe(page, 102);
  await clickNode(page, 102);
  const afterSelect = await waitForStableObserver(page, { requireProjectedBounds: true });
  const selectedNode = afterSelect.nodes.find((node) => node.personId === 102);
  const selectedLabel = (await waitForMeasuredLabels(page, [102])).labels.find(
    (label) => label.personId === 102
  )?.rect;
  const selectedProbe = await readLabelLifecycleProbe(page);
  if (!selectedNode || !selectedLabel || !selectedProbe.firstVisible || !selectedProbe.settled) {
    throw new Error("Selection did not produce complete label lifecycle evidence.");
  }
  expect(afterSelect.selectedPersonIds).toEqual([102]);
  expect(selectedProbe.remounted).toBe(true);
  expect(selectedProbe.mountedVisibility).toBe("hidden");
  expectLabelRectsToMatch(selectedProbe.firstVisible, beforeLabel, "first selection");
  expectLabelRectsToMatch(selectedProbe.settled, beforeLabel, "settled selection");
  expect(afterSelect.camera).toEqual(before.camera);
  expect(selectedNode.logicalPosition).toEqual(beforeNode.logicalPosition);
  expectLabelRectsToMatch(selectedLabel, beforeLabel, "selection");

  await startLabelLifecycleProbe(page, 102);
  await clickNode(page, 102);
  const afterDeselect = await waitForStableObserver(page, { requireProjectedBounds: true });
  const deselectedNode = afterDeselect.nodes.find((node) => node.personId === 102);
  const deselectedLabel = (await waitForMeasuredLabels(page, [102])).labels.find(
    (label) => label.personId === 102
  )?.rect;
  const deselectedProbe = await readLabelLifecycleProbe(page);
  if (
    !deselectedNode ||
    !deselectedLabel ||
    !deselectedProbe.firstVisible ||
    !deselectedProbe.settled
  ) {
    throw new Error("Deselection did not produce complete label lifecycle evidence.");
  }
  expect(afterDeselect.selectedPersonIds).toEqual([]);
  expect(deselectedProbe.remounted).toBe(true);
  expect(deselectedProbe.mountedVisibility).toBe("hidden");
  expectLabelRectsToMatch(deselectedProbe.firstVisible, beforeLabel, "first deselection");
  expectLabelRectsToMatch(deselectedProbe.settled, beforeLabel, "settled deselection");
  expect(afterDeselect.camera).toEqual(before.camera);
  expect(deselectedNode.logicalPosition).toEqual(beforeNode.logicalPosition);
  expectLabelRectsToMatch(deselectedLabel, beforeLabel, "deselection");
});

test("selected-node drag changes logical position and undo restores it", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  await page.getByRole("button", { name: "Move" }).click();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  await page.getByRole("button", { name: "Multi-select" }).click();
  await expect(page.getByRole("button", { name: "Single-select" })).toBeVisible();

  const before = await readObserver(page);
  const node = before.nodes.find((candidate) => candidate.personId === 102);
  if (!node) throw new Error("Projected fixture node was not rendered.");
  await clickNode(page, 102);
  expect((await readObserver(page)).selectedPersonIds).toEqual([102]);
  await page.mouse.move(node.cssPosition.x, node.cssPosition.y);
  await page.mouse.down();
  await page.mouse.move(node.cssPosition.x + 40, node.cssPosition.y, { steps: 4 });
  await page.mouse.up();

  const after = await waitForFrameAfter(page, before);
  const beforeNode = before.nodes.find((candidate) => candidate.personId === 102);
  const afterNode = after.nodes.find((candidate) => candidate.personId === 102);
  if (!beforeNode || !afterNode) throw new Error("Dragged fixture node was not observed.");
  expect(after.selectedPersonIds).toEqual([102]);
  expect(afterNode.logicalPosition).not.toEqual(beforeNode.logicalPosition);
  await expect(page.getByRole("button", { name: "Undo" })).toBeEnabled();

  await page.getByRole("button", { name: "Undo" }).click();
  await expect
    .poll(
      async () =>
        (await readObserver(page)).nodes.find((candidate) => candidate.personId === 102)
          ?.logicalPosition,
      { timeout: 10_000, intervals: [100, 200, 500] }
    )
    .toEqual(beforeNode.logicalPosition);
});

test("background drag orbits the camera without changing logical node positions", async ({
  page,
}) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  const before = await readObserver(page);
  const logicalBefore = before.nodes.map((node) => [node.personId, node.logicalPosition]);
  const x = before.canvas.left + 40;
  const y = before.canvas.top + 40;

  await page.mouse.move(x, y);
  await page.mouse.down();
  await page.mouse.move(x + 80, y + 20, { steps: 4 });
  await page.mouse.up();
  const after = await waitForFrameAfter(page, before);

  expect(after.camera.position).not.toEqual(before.camera.position);
  expect(after.nodes.map((node) => [node.personId, node.logicalPosition])).toEqual(logicalBefore);
});

test("wheel zoom changes the camera without changing logical node positions", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  const before = await readObserver(page);
  const logicalBefore = before.nodes.map((node) => [node.personId, node.logicalPosition]);

  await page.mouse.move(
    before.canvas.left + before.canvas.width / 2,
    before.canvas.top + before.canvas.height / 2
  );
  await page.mouse.wheel(0, -500);
  const after = await waitForStableObserver(page);

  expect(after.camera.position).not.toEqual(before.camera.position);
  expect(after.nodes.map((node) => [node.personId, node.logicalPosition])).toEqual(logicalBefore);
});

test("save download can be reimported with representative graph data intact", async ({ page }) => {
  await page.goto("/");
  await importFixture(page, familyFixture);
  const before = await readObserver(page);
  const downloadPromise = page.waitForEvent("download");

  await page.getByRole("button", { name: "Save" }).click();
  const download = await downloadPromise;
  const savedPath = await download.path();
  if (!savedPath) throw new Error("Save did not provide a downloadable file path.");

  await importFixture(page, savedPath);
  const after = await readObserver(page);
  expect(after.nodes.map((node) => node.personId).sort()).toEqual(
    before.nodes.map((node) => node.personId).sort()
  );
  await clickNode(page, 101);
  await expect(page.getByRole("dialog")).toContainText("Ada North");
  await expect(page.getByRole("dialog")).toContainText("January 2, 1940");
});

test("warning and invalid imports use the real browser messages without replacing the scene on error", async ({
  page,
}) => {
  await page.goto("/");

  await importFixture(page, warningFixture);
  const sessionBeforeInvalid = (await readObserver(page)).session;
  await expect(page.getByRole("status")).toContainText("Imported with warnings");
  await page.getByRole("button", { name: "Dismiss warnings" }).click();
  await expect(page.getByRole("status")).toBeHidden();

  await page.locator('input[type="file"]').setInputFiles(invalidFixture);
  await expect(page.getByRole("alert")).toBeVisible();
  expect((await readObserver(page)).session).toBe(sessionBeforeInvalid);
  await page.getByRole("button", { name: "Dismiss error" }).click();
  await expect(page.getByRole("alert")).toBeHidden();
});
