# Asset integration

## Establish the handoff

Before changing imports, obtain the intended asset role and the conventions already used in the project. For a new convention, agree on the decisions that affect placement, animation, collision, and asset reuse.

| Area | Required information when relevant |
| --- | --- |
| Placement | Physical dimensions, unit scale, root orientation, pivot, modular grid |
| Structure | Static versus moving parts, attachment sockets, chunk boundaries |
| Rendering | Shared materials, texture channels/sizes, alpha behavior, UV channels |
| Physics | Collider shapes, interaction bounds, navigation footprint |
| Animation | Clip names, timing, looping, skeleton/root-motion convention |
| Cost | Evaluated/exported geometry, material surfaces, textures, LOD strategy |

Use an explicit GLB handoff when it avoids relying on Blender availability in the engine import environment. Preserve the editable `.blend` separately. Direct `.blend` import can be appropriate for an established workflow; do not convert every existing asset without a reason.

Blender and Godot use different coordinate conventions. Let the exporter/importer perform their documented conversion; avoid a second guessed rotation on the asset root. Compare an asymmetric asset with a known dimension and pivot during an authorized import. A symmetric cube can hide orientation mistakes.

## Keep imports replaceable

Keep gameplay scripts, interaction state, and engine-specific configuration in a wrapper or suitable inherited scene so re-exporting the asset does not erase them. Use supported material overrides and import settings. Do not edit engine-generated import-cache files.

Keep semantic IDs independent of mesh names and render batching. A door may have separate hinge/leaf geometry and a stable gameplay identity; a shared decorative batch should not become the authority for repairs or inventory.

Do not import presentation cameras, reference images, lights, or high-detail source meshes unless they are intentional deliverables. Blender render lights and world/compositor settings do not establish the engine's lighting setup.

## Materials and lighting

Check exported PBR channels, texture color spaces, normal-map convention, UV assignment, and alpha mode. Reuse engine material resources deliberately. Distinct materials or surfaces can increase submission cost even when two props look similar.

Procedural Blender detail needs an exportable representation. Do not assume an arbitrary shader node graph, displacement setup, compositor effect, or animated material transfers to Godot. Confirm supported glTF extensions and the Godot importer/rendering path before relying on them.

Use UV1 for surface texturing and a unique padded UV2 where baked lightmaps require it. Mirrored/overlapping texture UVs can be intentional; lightmap UVs need a separate policy. Import-generated UV2 is an alternative when authorized, not a reason to overwrite source texture UVs.

Preserve toggled or moving machinery as appropriate geometry/state. Fully static lightmaps cannot correctly represent every power or destruction state. Keep the lighting split consistent with [mobile performance](mobile-performance.md).

## Physics and interaction

Use simple colliders appropriate to the asset's role. Static environment collision and moving-body collision need different strategies. Keep interaction areas and navigation clearance consistent with the physical dimensions, not decorative protrusions alone.

Godot import suffixes can generate special nodes, but they are optional conventions. Check the exact suffix behavior for the chosen release and avoid accidental collision/navigation generation from arbitrary source names.

## Authorized integration checks

Inspect scale/orientation, pivot rotation, snapping, material appearance, tangent seams, texture references, collider alignment, animation clips, and reimport behavior according to the change. For platform claims, inspect the selected native renderer on the intended operating system/device when permitted.

A GLB that exports successfully is not proof of correct Godot appearance or performance. If import or runtime execution is unavailable, report the handoff assumptions and the checks still needed rather than marking compatibility as verified.

Use the matching version of [3D scene import guidance](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/index.html) and [import configuration](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/import_configuration.html).
