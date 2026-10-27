import { expect, test } from "@playwright/test";
import { waitForStableObserver } from "./observer";

const warningFixture = "tests/e2e/fixtures/desktop-warning.json";

test("import warning banner", async ({ page }) => {
  await page.goto("/");
  await waitForStableObserver(page);
  await page.locator('input[type="file"]').setInputFiles(warningFixture);
  const warning = page.getByRole("status");
  await expect(warning).toContainText("Imported with warnings");
  await expect(warning).toHaveScreenshot("mobile-import-warning.png", {
    animations: "disabled",
    caret: "hide",
  });
});

test("Move mode toolbar", async ({ page }) => {
  await page.goto("/");
  await waitForStableObserver(page);
  await page.getByRole("button", { name: "Move" }).tap();
  await expect(page.getByRole("button", { name: "View" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Undo" })).toBeDisabled();
  await expect(page.getByRole("button", { name: "Multi-select" })).toBeVisible();
});
