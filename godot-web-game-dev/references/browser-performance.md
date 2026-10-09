# Browser performance

## Establish a usable budget

Record the target browser/device class, rendering resolution, desired frame rate, representative camera view, and stress case. Frame time in milliseconds is `1000 / FPS`. Allocate headroom across rendering, physics, gameplay, and loading instead of treating the full frame as a triangle allowance.

Track relevant costs separately: visible evaluated triangles/vertices, material surfaces and passes, draw calls, shadow casters, overlapping lights, transparency coverage, texture memory, active physics bodies, and script work. Asset file size measures transfer/storage, not GPU memory or frame time.

When profiling is authorized, compare the same scene, camera, population, resolution, browser, and device. Note whether the bottleneck is CPU, GPU, memory, or loading before changing geometry. Without measurements, describe the proposed change as an estimate.

## Godot 4 web constraints

The documented Godot 4 web path uses WebGL 2 and Compatibility. Forward+ automatic instancing is not available in Compatibility; use an explicitly supported approach such as manual MultiMesh where appropriate. Verify these constraints for the selected release. [Web export](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html), [3D optimization](https://docs.godotengine.org/en/stable/tutorials/performance/optimizing_3d_performance.html).

Choose threaded or single-threaded export from the target version and host capabilities. A threaded export requires browser isolation/security conditions, commonly COOP/COEP headers. Verify embedding and third-party resource behavior before changing host settings. Do not assume a local server is a deployment configuration. [Web export requirements](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_web.html#serving-the-files).

Do not substitute desktop-only renderer features or language/export support. If a required feature is unsupported, make the tradeoff explicit and keep the fallback scoped to that feature.

## Visibility and spatial organization

Godot already performs frustum culling. Hidden geometry can still consume scene, physics, simulation, and memory costs; drawing fewer objects is only one layer of optimization. [Culling](https://docs.godotengine.org/en/stable/tutorials/performance/optimizing_3d_performance.html#culling).

Partition a large environment into rooms or spatial chunks that fit the camera and streaming plan. Keep bounds accurate. One giant factory mesh or batch can prevent useful granular culling; one object per bolt can create too much node and submission work. Group nearby static surfaces by material and practical visibility boundaries.

Do not hide a room merely because the player occupies another one. Doors, windows, camera overlap, and a top-down view can make neighboring spaces visible. Keep a margin or overlap where transitions would otherwise pop. Roof/wall cutaways are an art and gameplay decision, not an automatic performance rule.

Use occlusion culling when substantial opaque geometry really blocks the camera view. An overhead view may offer little useful occlusion. Confirm benefit before adding complex occluders, and never use culling to introduce an unapproved vision cone or change a darkness mechanic. [Occlusion culling](https://docs.godotengine.org/en/stable/tutorials/3d/occlusion_culling.html).

## Repetition and LOD

Reuse meshes and material resources. For repeated noninteractive props, consider MultiMesh. Split batches by room/chunk because visibility applies to a whole MultiMesh, rather than independent instance frusta. Material surfaces and render passes still affect its draw cost. [MultiMesh](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html).

Keep persistent interaction IDs separate from render-instance indices. Rebatching, removing an instance, or changing its visibility must not change inventory or job identity. Give an interactive repeated object a suitable collision/interaction representation without duplicating every decorative part as a gameplay node.

Use mesh LOD for geometry detail and visibility ranges/HLOD for distant groups or effects. A fixed orthographic camera may offer little distance-driven benefit; judge detail by projected size and the camera's actual movement or zoom. Make transitions intentional, with no accidental rendering of every manual LOD at once. [Mesh LOD](https://docs.godotengine.org/en/stable/tutorials/3d/mesh_lod.html), [visibility ranges](https://docs.godotengine.org/en/stable/tutorials/3d/visibility_ranges.html).

Pool objects when allocation/destruction churn is a demonstrated problem. Reset state, signals, timers, and ownership on reuse. Pooling every scene preemptively can retain unnecessary memory and complicate lifetimes.

## Industrial realism at lower cost

Keep geometry for silhouette, openings, moving parts, interaction boundaries, and collision needs. Put scratches, small fasteners, panel seams, and surface wear into reusable textures or baked normal detail when the gameplay camera allows it. Compare the texture/shader cost with the geometry saved.

Prefer shared opaque materials, trim sheets, or atlases where they preserve sufficient detail. Minimize overlapping alpha surfaces and unnecessary material variants. Reduce transparent coverage and passes before assuming lower triangle counts solve fill-rate problems.

Use baked lighting for stable parts of the environment when it matches gameplay. Compatibility can render supported baked lightmaps, while baking requires a suitable desktop rendering setup. Supply unique padded UV2 through the asset or an authorized import unwrap. [Renderer support](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html), [LightmapGI](https://docs.godotengine.org/en/stable/tutorials/3d/global_illumination/using_lightmap_gi.html).

Baked light cannot automatically respond to changing power, doors, destruction, or lamps. For such systems, choose a documented static/dynamic split, an authored lighting-state solution, or simpler materials. Budget dynamic lamps and shadow ranges for the actual visible scene; emissive appearance alone does not illuminate nearby objects.

## Simulation, loading, and evidence

Pause or reduce offscreen cosmetic animation and effects independently of authoritative state. Schedule expensive nonvisual work according to its required fidelity. Read [architecture and state](architecture-and-state.md) before changing background job processing.

Stream or preload chunks from the traversal plan, preserving needed collision, navigation, jobs, and state. Define reattachment and boundary behavior. Culling does not unload resources, and streaming does not itself preserve simulation.

When browser exports are authorized, account for compressed transfer, startup latency, texture upload, and cache/version behavior. A file-size estimate does not establish total load time. Browser performance claims need results from the intended export and target device, not a Blender preview or desktop editor view.

Use documented profiler and `Performance` monitors for the selected release. State units and limitations; some monitors are debug-only. Do not invent monitor constants or assume that rendering and script timings add up without considering their measurement semantics. [Performance API](https://docs.godotengine.org/en/stable/classes/class_performance.html).
