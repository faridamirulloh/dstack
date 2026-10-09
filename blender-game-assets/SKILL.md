---
name: blender-game-assets
description: Create or refine editable Blender game assets, bake surface detail, export scoped GLBs, and render asset previews. Use for runtime models and their source-to-engine handoff.
---

# Blender game assets

Create editable models that read clearly from the gameplay camera and have a deliberate runtime cost. Support low-triangle realism through silhouette geometry, reusable materials, and baked detail. Keep preview rendering separate from the engine asset.

## Establish the asset context

Read applicable instructions, the asset brief, references, and existing source conventions. Determine whether the task is modeling, modifying an existing asset, materials/baking, export, animation, reference fitting, or preview rendering. A request for a preview alone does not imply creating a complete game-ready model.

Establish the Blender version, target engine/version/renderer, dimensions and unit scale, camera/viewing distance, pivots and modular grid, moving parts, material/texture conventions, collision needs, and intended deliverables. Read project records before asking. Missing details remain explicit assumptions unless they block scale, compatibility, or another material decision.

Derive geometry, texture, material, and animation budgets from the visible population and target devices. Do not treat a fixed triangle count, 1024-pixel texture, or GLB file-size limit as proof of browser performance. If budgets are missing, propose provisional values and label them as estimates.

Use available Blender/editor, CLI, or MCP capabilities. Do not assume a specific MCP server or provider-specific tool name. Follow project permissions for scripts, renders, bakes, exports, imports, dependencies, and runtime checks. If execution is unavailable, provide the permitted modeling instructions or script draft and state which asset deliverables remain unproduced.

## Choose the relevant guidance

Read only the references needed for the task.

| Task | Reference |
| --- | --- |
| Meshes, modular kits, scale, pivots, evaluated geometry | [Modeling](references/modeling.md) |
| PBR materials, atlases, UV coverage, baking | [Materials and baking](references/materials-and-baking.md) |
| Scoped GLB output and Godot handoff | [Export to Godot](references/export-to-godot.md) |
| Gameplay-view and presentation previews | [Preview rendering](references/preview-rendering.md) |
| Rigid machinery or skeletal animation | [Animation](references/animation.md) |
| Blueprint, image, silhouette, or multiview fitting | [Reference fitting](references/reference-fitting.md) |
| Upstream attribution and authoritative documentation | [Sources](references/sources.md) |

## Preserve the source and work deliberately

Scope operations to the named asset, selection, or collection. Preserve unrelated objects, materials, worlds, cameras, and render settings. Do not clear the scene or assume the default cube is disposable in an existing file.

Keep an editable `.blend` source. Use a scoped export copy where applying modifiers, decimating, triangulating, or changing transforms would destroy useful editability. Shared mesh or material data must be copied when only one instance should change.

Allocate geometry to silhouette, openings, articulation, and interaction needs. Use baked or reusable surface detail for features too small to justify runtime geometry. Preserve modular reuse and practical room/chunk boundaries rather than merging an entire environment for a nominally lower object count.

Check Blender APIs, node sockets, render properties, and exporter options against the actual release. Do not copy obsolete EEVEE settings, alpha properties, or operators from an unrelated version. Prefer capability checks in automation when several versions must be supported.

## Deliver and describe the actual result

Produce the requested subset of editable source, textures, scoped GLB, animation clips, and previews. Keep output locations within the agreed scope. An export does not establish engine compatibility; a Blender render does not establish browser performance.

Report dimensions/pivots, exported scope, material/texture choices, relevant geometry counts with their measurement stage, checks actually performed, and unresolved handoff assumptions. Make repairs against concrete defects and stop when the requested outcome is met or blocked. Do not start an automatic skill-rewriting, dependency, or release workflow.

This skill adapts reviewed MIT-licensed material from `RobLe3/cc-blender-skill`; see [sources](references/sources.md) and [LICENSE](LICENSE).
