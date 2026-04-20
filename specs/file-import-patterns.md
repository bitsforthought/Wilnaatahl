# File Import Patterns for Cross-Platform Web Apps

Notes on how a single-page, purely client-side web app can let users import
files (e.g. genealogical data) across Windows, macOS, iOS, and Android.

## The universal approach: `<input type="file">`

A standard file input works **identically** on Windows, macOS, iOS, and Android,
in every major browser. When tapped/clicked, the OS opens its native file picker:

- **Desktop**: Finder / File Explorer
- **iOS**: Files app (with iCloud Drive, Dropbox, Google Drive providers)
- **Android**: Storage Access Framework picker (Drive, Downloads, etc.)

```html
<input type="file" accept=".ged,.json" />
```

This is the **only** approach guaranteed to work everywhere with zero fallbacks.
Usually styled as a button (`<label for="...">Import file</label>` with the
input visually hidden).

## Drag-and-drop: desktop only

The HTML5 Drag and Drop API (`ondrop`, `ondragover`, `DataTransfer.files`)
works on desktop browsers but is **effectively unavailable on mobile**:

- iOS Safari: no support for dragging files from other apps into a browser tab
- Android Chrome: same — no OS-level file drag into the browser

So drag-and-drop is a **desktop enhancement**, not a primary input method.

## The recommended cross-platform pattern

A single drop zone that **also** acts as a click target wrapping a hidden file
input:

- **Desktop users**: drag a file onto the zone _or_ click to browse
- **Mobile users**: tap the zone to open the native picker

This is what GitHub, Figma, Notion, etc. do. One UI element, both interaction
models, works everywhere.

## Other options (less suitable here)

- **File System Access API** (`showOpenFilePicker`) — nicer ergonomics and
  read/write handles, but Chromium-only. No Safari (desktop or iOS), no
  Firefox. Fine as a progressive enhancement.
- **Web Share Target API** — lets your app receive files shared _from_ other
  apps, but requires PWA installation and is Android/Chrome-only in practice.
- **Paste from clipboard** (`onpaste`) — useful for images/text, awkward for
  genealogy files.

## Recommendation for Wilnaatahl

Build one "Import" component:

1. Hidden `<input type="file" accept="...">` — the universal mechanism
2. A styled drop zone wrapping it that handles `dragover`/`drop` for desktop
   bonus UX
3. Optionally feature-detect `window.showOpenFilePicker` and prefer it on
   Chromium for the nicer "remember this file" handle

Both paths converge on a `File` object → `file.text()` or `file.arrayBuffer()`
→ parser → F# domain model. Pure client-side, no server needed.
