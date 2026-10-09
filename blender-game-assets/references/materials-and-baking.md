# Materials and baking

## Reusable exportable materials

Start from the target engine's material capabilities. Use a compact shared palette, trim sheets, or atlases when they preserve the necessary variation. Avoid a unique material and full texture set for every repeated bolt or panel.

Separate surface realism from material complexity. Roughness variation, plausible metal/paint relationships, readable wear, and baked normals can provide useful industrial detail without high subdivision or expensive layered runtime shaders.

Use an exportable PBR arrangement and verify the exporter-supported channels. Bake procedural detail that cannot transfer through the chosen format. A connected Blender shader does not prove it will have an equivalent engine shader. [Blender glTF materials](https://docs.blender.org/manual/en/5.1/addons/import_export/scene_gltf2.html#materials).

Use appropriate color spaces: color information such as base color/emission uses a color transform, while normal, roughness, metallic, and packed data maps use non-color data treatment. Use a tangent-space Normal Map node for a tangent normal texture, not a height-oriented Bump node. Confirm the normal convention through the exporter and engine.

Metallic values need to reflect the surface. Pure metal and dielectric regions often have distinct endpoints, but mixed pixels, coatings, and transitions can use intermediate values. Do not impose a universal binary-metallic rule or add a separate emission shader when the supported Principled/PBR channel meets the need.

Prefer opaque materials when possible. Keep cutouts, glass, and alpha blending deliberate because overlapping transparency adds runtime work. Choose alpha properties and effects supported by the actual Blender release and engine renderer.

## UVs, coverage, and atlases

Choose texel density from camera distance, asset size, and expected detail. UV islands can share texture space intentionally for repeated/mirrored surfaces; avoid overlaps where unique wear, bake projection, or other requirements need separate pixels.

For an atlas, reserve meaningful regions and choose padding for the texture resolution, filtering, and mip levels. Dilate baked colors into margins to limit seams and neighboring-region bleed. A packed atlas without mip-safe margins is not finished.

Inspect the asset's intended surfaces, including sides, backs, undersides, and newly visible damage/animation states. An image node anywhere in a material is not evidence that a face samples it. Trace the connected shader path, assigned material, UV layer, and island coverage.

Scope coverage inspection explicitly. An empty selection must be reported as no inspected geometry, rather than zero defects. Do not rely on mascot-specific prefixes or count disconnected image nodes as valid texture coverage.

If Godot lightmaps are planned, reserve a separate UV2 with unique nonoverlapping islands and suitable padding, or agree on import-generated UV2. Keep this separate from texture UV1; do not silently destroy useful mirrored texture layout. [Godot lightmap UV requirements](https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/using_lightmap_gi.html).

## Bake surface detail deliberately

Define the source detail, destination mesh, maps, resolution, UV layer, and projection/cage before baking. Check scale and the source/destination relationship. Large projection distances can capture neighboring surfaces; small distances can miss recesses.

For a multi-material destination, ensure each participating material has the correct active target image/node for the bake. Handling only the first material can leave other surfaces missing. Save generated images to the agreed location and preserve their references in the editable source.

Bake the maps the runtime material actually needs. Separate base color from lighting unless a baked-lit/unlit style is explicitly intended. Do not bake dynamic power states or moving shadows into a surface that must respond to gameplay lighting.

For tangent normal baking, keep the mesh triangulation, UVs, shading normals, and tangent convention consistent with export. Check hard-edge and mirrored-island seams when inspection is authorized. A normal map improves surface shading, not silhouette or collision.

Use the appropriate release's [Cycles baking guidance](https://docs.blender.org/manual/en/latest/render/cycles/baking.html). Check the engine/export interpretation of AO and any packed channels; do not assume the exporter infers a custom node graph or arbitrary channel layout.

## Texture cost and deliverables

Choose texture dimensions, bit depth, encoding, compression, and mip behavior from the target asset and engine. GPU memory depends on format, dimensions, mips, and residency, not merely PNG/JPEG file size. A larger atlas can reduce material variants while retaining more unused pixels in memory.

Keep source textures and runtime textures identifiable without duplicating unrequested output formats. Avoid UDIM or special material extensions unless the chosen export/import/runtime path supports them.

Report map names/channels, dimensions, UV policy, material reuse, baked-versus-runtime detail, and any appearance checks still pending in the target engine. Do not call texture coverage or engine appearance verified from node existence alone.
