# Animation

## Choose the minimum motion representation

Determine which parts move, why they move, and what the engine must control. Doors, shafts, conveyors, and rigid robot joints may need simple transform clips rather than a dense deforming mesh. Use an armature where deformation or coordinated articulation requires it.

Define the root, local axes, hinges, attachment sockets, rest pose, clip names, duration, frame/sample rate, loop policy, and any root motion. Keep the root-motion contract separate from gameplay-driven movement so the engine does not apply the displacement twice.

## Preserve editable control

Keep useful constraints, drivers, and rig controls in the source. Export only the representation the runtime supports. Bake procedural or constrained motion to suitable keyframes when required, checking the selected release's action/NLA/export handling.

Do not assume a Python frame handler, driver, constraint graph, Geometry Nodes system, material animation, or UV animation survives a GLB export. Some properties may use glTF extensions; support must be confirmed for the Blender exporter, Godot importer, and renderer. Otherwise choose an approved engine-side implementation or a supported baked representation.

Applying transforms or modifiers to an existing rig can change skinning, shape keys, pivots, and animation. Preserve the authoring setup and use an export copy where appropriate. Handle shared data deliberately.

## Runtime cost

Budget bones, skin influences, blend shapes, evaluated geometry, clip count, key density, and simultaneously visible actors from the game camera and target device. A low-triangle mesh with many animated actors can still be expensive.

Remove redundant keys when the visible motion stays within the chosen tolerance. Preserve contact poses, hinge limits, tool alignments, and important timing. No fixed frame-rate or bone-count rule fits every asset.

The engine can reduce offscreen cosmetic animation independently of gameplay simulation. Provide clip/state information that lets it restore an appropriate pose without replaying a gameplay effect twice.

## Checks when authorized

Inspect rest pose, range of motion, contact and clearance, attachment transforms, deformation, root displacement, loop boundary, and clip separation according to the asset. For a loop, compare position, orientation, and relevant velocity across the boundary; a matching first/last image alone can hide a hitch.

Use the gameplay view plus a closer diagnostic view when necessary. Preview the exported/imported animation only when those checks are authorized. A correct source timeline does not establish that all requested clips arrived in the engine.

Report source rig requirements, exported clips, loop/root-motion conventions, and any export/import checks still pending. Use the project's Blender release for the [animation manual](https://docs.blender.org/manual/en/latest/animation/index.html) and [glTF animation guidance](https://docs.blender.org/manual/en/5.1/addons/import_export/scene_gltf2.html#animation).
