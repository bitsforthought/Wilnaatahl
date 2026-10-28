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

  // Hide DOM overlays so labels cannot make a blank WebGL scene pass.
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
      const channelsPerPixel = 4;
      const redChannelOffset = 0;
      const greenChannelOffset = 1;
      const blueChannelOffset = 2;
      const alphaChannelOffset = 3;

      // getImageData packs RGBA bytes; slice copies the top-left pixel as the background reference.
      const background = pixels.slice(0, channelsPerPixel);

      for (
        let pixelOffset = channelsPerPixel;
        pixelOffset < pixels.length;
        pixelOffset += channelsPerPixel
      ) {
        if (
          pixels[pixelOffset + redChannelOffset] !== background[redChannelOffset] ||
          pixels[pixelOffset + greenChannelOffset] !== background[greenChannelOffset] ||
          pixels[pixelOffset + blueChannelOffset] !== background[blueChannelOffset] ||
          pixels[pixelOffset + alphaChannelOffset] !== background[alphaChannelOffset]
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
