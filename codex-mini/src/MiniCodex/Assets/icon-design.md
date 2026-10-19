# Mini Codex icon

Selected design: **option 3, Stacked windows**, matching the user's chosen image. The original is preserved at `icon-options/03-stacked-windows.png`; `mini-codex.png` is an unchanged copy with its original transparency.

The Windows ICO exports that design at 16, 20, 24, 32, 40, 48, 64, 128, and 256 pixels using PNG frames with alpha. The export fits the visible artwork to approximately 98% of the canvas, removing the original transparent outer padding so the taskbar icon matches the size of other Windows apps. Format conversion uses Windows System.Drawing; the original PNG and artwork are preserved.

The project uses `mini-codex.ico` as its executable icon and as a WPF resource for window icons. No build or application launch was performed for this icon update.

## Generation prompt

The design was generated with the built-in image generation tool. Its [original prompt](icon-options/prompts.md#3-stacked-windows) is retained with the alternatives. No image generation was needed to apply this selection.
