# Sources and adaptation

Reviewed source: [RobLe3/cc-blender-skill](https://github.com/RobLe3/cc-blender-skill/tree/11016c9a5847897491dde935c346571bd7548e3d), commit `11016c9a5847897491dde935c346571bd7548e3d`.

All 30 skills in the source's `plugin/skills` library were reviewed. This draft adapts selected instructions without importing its provider-specific router, MCP dependency, executable helpers, automatic skill-editing loops, or release workflows. The source copyright and MIT permission notice are preserved in [LICENSE](../LICENSE).

Core topics retained: staged modeling, materials, UVs, atlases, connected texture coverage, lighting, cameras, rendering, export, animation, and asset/preview coordination. Their source-faithfulness, scene-destruction, subdivision, export-scope, version, and fixed-budget assumptions were revised for reusable game assets.

Optional reference fitting combines selected contour, registration, landmark, multiview, texture-fit, and repair ideas. The reviewed contour/coverage/analyzer helpers were not copied because their documented capabilities and implementation did not reliably match. Mascot/logo reconstruction, orbital HUD motion, and automatic refinement loops are deferred.

Authoritative entry points include the [Blender manual](https://docs.blender.org/manual/en/latest/), [Blender API](https://docs.blender.org/api/current/), [glTF exporter manual](https://docs.blender.org/manual/en/5.1/addons/import_export/scene_gltf2.html), [glTF specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html), and [Godot 3D import guidance](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/importing_3d_scenes/index.html).

Select documentation for the actual Blender and engine releases. `latest`, `current`, and `stable` can move; the explicit 5.1 exporter link is a reviewed reference, not a required Blender version. This instruction-only draft was prepared on 8 October 2026; it contains no executable helpers and does not claim runtime validation.
