/**
 * Serializes JSON text into a browser download and releases the temporary URL
 * after the browser has been asked to start the download.
 */
export function downloadJson(json: string, filename: string): void {
  const blob = new Blob([json], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.appendChild(anchor);

  try {
    anchor.click();
  } finally {
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
  }
}

/**
 * Gets the first file selected by a file input and clears the input so selecting
 * that same file again produces another change event.
 */
export function firstSelectedFile(input: HTMLInputElement): File | undefined {
  const file = input.files?.[0];
  input.value = "";
  return file;
}
