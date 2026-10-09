# Export to Godot

## Preserve the authoring asset

Retain the editable `.blend`, source textures, and useful modifier/rig setup. Use a scoped export copy when conversion, transform application, triangulation, or decimation would remove editability. Do not rewrite the source merely to match a one-off export setting.

Choose GLB for a self-contained handoff when it fits the project. Confirm whether textures are embedded or external and whether the engine should share external materials. Direct `.blend` import is an alternative for an established pipeline; it introduces Blender availability/version into the import environment.

## Define the exported scope

Export the intended asset collection or explicit selection, including necessary ancestors, armatures, sockets, and dependencies. Use exporter options that exist in the installed version. Never assume selection-only export is active, and do not export the whole scene by default.

Exclude reference planes, modeling guides, high-detail bake sources, preview rigs, and unrelated assets. Include cameras or punctual lights only when the handoff explicitly requires them. Blender world, Area-light, compositor, and render settings should not be presented as a portable Godot lighting setup.

Preserve meaningful part hierarchy, pivots, names, and attachment transforms. Do not flatten every machine merely to reduce node count when doors, hinges, or robot parts need independent motion or identity. Coordinate static batching with the engine's room/chunk boundaries.

## Geometry, axes, and materials

Record physical dimensions, Blender unit convention, root/pivot, and expected engine orientation. Let the documented export/import conversion handle Blender Z-up and glTF/Godot Y-up conventions; avoid applying a second guessed axis correction.

Choose modifier evaluation deliberately. Not every rig or shape-key workflow can use the same apply-modifiers setting. Stabilize geometry used for normal baking and export, while preserving the editable source.

Include the necessary normals, UVs, tangents, and exportable material inputs. Verify textures are saved and resolvable. Mesh counts and source polygons do not establish the exported GPU cost; seams and material boundaries can change the representation. [Blender glTF export guidance](https://docs.blender.org/manual/en/5.1/addons/import_export/scene_gltf2.html).

Confirm extension support through the entire pipeline before using GPU-instancing, compression, texture transforms, advanced materials, or animated material extensions. An exporter checkbox is not evidence that the Godot importer and Compatibility renderer support the result.

## Engine responsibilities

Keep gameplay state and scripts in an engine wrapper or suitable inherited scene so an asset re-export can replace visual data. Preserve stable gameplay IDs independently of mesh names and instance ordering.

Provide simple collider geometry or a documented collision intent. Engine import suffixes are optional, version-specific conventions; use them only when the project has adopted their behavior. Navigation and interaction areas also need an engine-side decision.

If lightmaps are planned, provide unique padded UV2 or identify the authorized Godot unwrap step. Static light baking occurs in an appropriate engine/editor setup; the `.blend` preview lighting is not that bake. Keep dynamic power states, moving lamps, doors, and robots consistent with the chosen static/dynamic split.

## Handoff information

Provide relevant facts in the existing asset record or the response, rather than generating an unrequested documentation package:

- Source and export paths, Blender version, and target engine/version/renderer.
- Dimensions, unit scale, orientation, root/pivot, and attachment conventions.
- Export scope and any excluded source/preview objects.
- Geometry counts labeled as source, evaluated, exported, or estimated.
- Material surfaces, texture maps/sizes, alpha behavior, and UV1/UV2 policy.
- Collider/navigation intent, animation clips, and supported extensions used.
- Checks performed and unresolved import/runtime assumptions.

## Checks when authorized

Inspect the produced GLB or import it through the intended workflow only when artifact inspection/import is in scope. Check an asymmetric dimensioned asset for orientation and scale, then pivots, seams, material channels, textures, hierarchy, collision, and animation as relevant.

An independent format viewer can reveal some export defects but does not prove Godot or browser behavior. An exported asset is deliverable evidence; target-engine appearance and browser performance are separate claims.

Use the target release's [Godot import configuration](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/import_configuration.html) and [renderer support](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html). Report checks that could not be performed without expanding the task.
