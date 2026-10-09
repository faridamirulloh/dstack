# Preview rendering

## Pick the preview's purpose

Use a gameplay-view preview to assess silhouette, readable detail, scale, and material separation at the intended camera angle/resolution. Use a neutral asset view for modeling inspection, or a presentation render when the user asks for one. Do not substitute a dramatic close-up for evidence about a distant top-down view.

A request for rendered images can be satisfied without a runtime model only when that is the agreed output. If editable meshes or GLBs are requested, image generation or a Blender screenshot alone does not complete the asset task.

## Scope the preview setup

Preserve the existing scene's camera, world, lights, color management, and render configuration. Use a separate preview scene/collection or record and restore targeted temporary changes when that is the permitted workflow.

Frame the explicit asset set. Compute bounds from its relevant evaluated geometry in world space, not all meshes with a guessed prefix across the entire file. Include animation extremes or intended assembly spacing when needed. Respect perspective versus orthographic composition and the requested aspect ratio.

Keep comparison previews consistent in camera, lighting, exposure, color transform, resolution, and object scale. A material or silhouette comparison becomes unreliable when each version uses a different presentation rig.

## Renderer and lighting

Choose EEVEE or Cycles from the preview goal, available hardware, and time budget. Check engine identifiers and settings against the installed Blender version. Do not reuse old EEVEE bloom/SSR/AO flags in a release that removed or changed them. [EEVEE 4.2 migration](https://developer.blender.org/docs/release_notes/4.2/eevee_migration/), [Blender rendering manual](https://docs.blender.org/manual/en/latest/render/index.html).

Use a simple controlled rig for material/model inspection. Add cinematic depth of field, volumetrics, compositing, or elaborate lighting only when they serve the requested image. These can conceal edges, consume render time, or imply effects the runtime cannot reproduce.

Set preview quality to resolve the decision being made. Increase samples or resolution for a specific visible defect, rather than prescribing a universal high-quality render setting. Do not assume GPU Cycles is available or change user hardware preferences without scope.

## Inspect and deliver when rendering is authorized

View the output at its intended size. Check framing, missing geometry/textures, blown highlights, crushed dark detail, shading seams, light leaks, and the feature the preview was meant to assess. Fix concrete problems in the scoped asset or preview rig.

Keep repairs bounded by the requested goal and render budget. If a new art decision or unavailable renderer blocks progress, state the unresolved issue rather than starting an endless render/refine loop.

Return the requested images and their paths. State the camera/render setup when it matters to comparison. A Blender render demonstrates that preview setup; it does not measure engine rendering, draw calls, browser FPS, or target-device memory use.
