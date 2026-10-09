# Modeling

## Scale, hierarchy, and reuse

Choose physical dimensions before adding detail. Use the project's established units, grid, snapping intervals, and reference objects. For a new Godot handoff, meters are a useful default to propose; confirm dimensions rather than guessing from a render.

Use a root and named asset collection that make selection/export scope unambiguous. Separate render geometry, source/detail geometry, colliders, sockets, and presentation helpers where these have different deliverables. Prefixes are optional conventions, not prerequisites for finding the asset.

Put pivots where placement or motion requires them: a floor/contact anchor for many props, a hinge for a door, a shaft axis for rotating machinery, or the agreed root for a robot. Preserve attachment transforms and clearances for replaceable parts.

Prefer modular wall/floor sections, machine families, reusable supports, and shared trim/material systems for repeated environments. Keep chunks compatible with the engine's visibility and streaming plan. One asset per decorative bolt and one merged mesh for a whole factory are both poor defaults.

Retain distinct moving or interactive parts even when static surfaces can be batched. Semantic identity does not need to equal mesh count; the engine can keep stable gameplay IDs while sharing render geometry.

## Geometry allocation

Block out silhouette, footprint, openings, articulation, and major intersections from the gameplay view. Give a foreground robot or interactable more silhouette detail than distant background machinery when the camera makes that useful.

For industrial assets, use geometry for larger pipes, handles, exposed shafts, recesses with visible depth, and structural damage that changes the outline. Consider texture/normal detail for small fasteners, panel grooves, scratches, labels, and wear. Check projected size instead of using realism as a reason to subdivide everything.

Choose curve resolution, cylinder side counts, and bevel segments by the visible silhouette. A small bevel can help shading, but subdivision and multi-segment bevels are not automatic game-asset defaults. Budget evaluated geometry after the complete modifier stack.

Remove truly inaccessible faces only when all gameplay views, damage states, reflections, and intended lighting permit it. A closed prop or a reversible part may need its back and underside. Do not turn a front-view reference into an arbitrary shallow slab.

## Editability and topology

Keep source modifiers when they help iteration. Apply destructive operations to an export copy when needed, preserving the authoring mesh and its relationships. Copy shared data before changing one instance independently.

Inspect degenerate faces, duplicate vertices, unintended internal surfaces, bad normals, and thin geometry according to the asset's role. Use closed topology where required; do not mandate watertight meshes for every decorative sheet or plane.

Triangulation changes shading and deformation. Choose a stable triangulation for baked normal maps or animated geometry when those depend on it. Preserve the relationship among UVs, normals, tangents, and the geometry used for the bake/export.

Use decimation to simplify a justified export mesh, not as a substitute for an intentional silhouette. Protect hinges, attachment surfaces, collision clearances, UV/material boundaries, and animated regions. Keep a separate LOD only when the engine/camera strategy benefits from it.

## Count the relevant geometry

Distinguish authoring faces, evaluated triangles, exported vertices, and visible scene totals. Modifiers can add geometry; UV seams and hard shading boundaries can split exported vertices. Do not report the source polygon count as the GPU vertex count. [Blender glTF mesh guidance](https://docs.blender.org/manual/en/5.1/addons/import_export/scene_gltf2.html#meshes).

Account for copies/instances and material surfaces when estimating a scene. Lower triangles do not necessarily lower draw calls. Reusing source mesh data is useful, but engine import/instancing behavior still needs confirmation.

## Collision and export readiness

Choose simple collider geometry separately from visual detail. A moving prop usually needs primitives or convex shapes; static architecture may use an appropriate static-mesh strategy. Agree on engine naming/import conventions before relying on automatic collider generation.

Do not apply transforms to an existing rig or hierarchy without understanding the animation and child effects. Normalize transforms for an export only with a defined pivot/root policy. Watch negative or nonuniform scale for shading, collision, and rigging issues.

Continue with [materials and baking](materials-and-baking.md) or [export to Godot](export-to-godot.md) as needed. The [Blender mesh manual](https://docs.blender.org/manual/en/latest/modeling/meshes/index.html) and [API reference](https://docs.blender.org/api/current/) must be read in the relevant version when an unfamiliar operator or property is required.
