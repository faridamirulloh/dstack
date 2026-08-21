# HTML Report Format

Render the architecture survey as one self-contained HTML file. It must work from disk with no network access: inline CSS and inline SVG only, with no CDN, external font, image, script, or stylesheet.

## Scaffold

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Architecture review for {{repo name}}</title>
    <style>
      :root { color-scheme: light; --ink:#172033; --muted:#64748b; --paper:#fafaf9; --card:#fff; --line:#cbd5e1; --accent:#047857; --warn:#b45309; --risk:#dc2626; }
      * { box-sizing: border-box; }
      body { margin:0; background:var(--paper); color:var(--ink); font:16px/1.5 system-ui,sans-serif; }
      main { width:min(1040px,calc(100% - 32px)); margin:0 auto; padding:48px 0 72px; }
      header, article, .recommendation { margin-bottom:32px; }
      article, .recommendation { background:var(--card); border:1px solid var(--line); border-radius:14px; padding:24px; }
      .badges { display:flex; gap:8px; flex-wrap:wrap; }
      .badge { border-radius:999px; padding:3px 9px; background:#e2e8f0; font-size:12px; }
      .diagrams { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:16px; }
      .diagram { min-height:260px; border:1px solid var(--line); border-radius:10px; padding:16px; overflow:auto; }
      code, .files { font-family:ui-monospace,monospace; }
      @media (max-width:720px) { .diagrams { grid-template-columns:1fr; } }
    </style>
  </head>
  <body><main>{{report}}</main></body>
</html>
```

## Structure

Start with repository name, date, and a compact legend. Then render one `<article>` per candidate with:

- candidate name and recommendation strength: Strong, Worth exploring, or Speculative;
- exact files and modules;
- one-sentence problem and one-sentence proposed deepening;
- side-by-side before and after diagrams;
- short benefits stated in locality, leverage, interface depth, and testability;
- an ADR warning when the proposal revisits a recorded decision.

End with one top recommendation and why it leads.

## Diagrams

Use accessible inline HTML and SVG. Give every SVG a `<title>` and concise text alternative. Pick the visual that matches the claim:

- boxes and arrows for dependencies or call flow;
- stacked bands for shallow layers;
- interface-versus-implementation rectangles for module depth;
- a collapsed call tree for consolidation;
- solid boxes for modules, dashed lines for seams, red arrows for leakage, and a thick dark box for a deep module.

Keep diagrams around 320 pixels tall and readable without interaction. Do not add JavaScript unless the user asks for an interactive report.

## Tone

Use `$architect` vocabulary: module, interface, implementation, depth, deep, shallow, seam, adapter, leverage, and locality. Keep prose sparse and concrete. The diagrams should carry the argument.
