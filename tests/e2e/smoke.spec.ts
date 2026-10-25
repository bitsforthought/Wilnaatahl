import { expect, test } from "@playwright/test";

test("boots the sample visualization with WebGL and rendered labels", async ({ page }) => {
  await page.goto("/");

  const canvas = page.locator("canvas");
  await expect(canvas).toBeVisible();
  await expect(page.getByText("The Mayor")).toBeVisible();

  const rendering = await canvas.evaluate((element) => {
    if (!(element instanceof HTMLCanvasElement)) return null;

    const context = element.getContext("webgl2");

    return {
      hasWebGL2: context !== null && !context.isContextLost(),
      width: context?.drawingBufferWidth ?? 0,
      height: context?.drawingBufferHeight ?? 0,
    };
  });

  expect(rendering).not.toBeNull();
  expect(rendering?.hasWebGL2).toBe(true);
  expect(rendering?.width).toBeGreaterThan(0);
  expect(rendering?.height).toBeGreaterThan(0);

  const canvasScreenshot = await canvas.screenshot({
    style: "body * { visibility: hidden !important; } canvas { visibility: visible !important; }",
  });
  const hasRenderedPixels = await page.evaluate(
    async (imageDataUrl) => {
      const image = new Image();
      image.src = imageDataUrl;
      await image.decode();

      const sampleCanvas = document.createElement("canvas");
      sampleCanvas.width = image.naturalWidth;
      sampleCanvas.height = image.naturalHeight;

      const context = sampleCanvas.getContext("2d");
      if (!context) return false;

      context.drawImage(image, 0, 0);
      const pixels = context.getImageData(0, 0, sampleCanvas.width, sampleCanvas.height).data;
      const background = pixels.slice(0, 4);

      for (let pixel = 4; pixel < pixels.length; pixel += 4) {
        if (
          pixels[pixel] !== background[0] ||
          pixels[pixel + 1] !== background[1] ||
          pixels[pixel + 2] !== background[2] ||
          pixels[pixel + 3] !== background[3]
        ) {
          return true;
        }
      }

      return false;
    },
    `data:image/png;base64,${canvasScreenshot.toString("base64")}`
  );

  expect(hasRenderedPixels).toBe(true);
});
