# Desktop performance

## Budget the actual target

Record the minimum and representative hardware, operating system, GPU/driver, renderer, resolution, display scaling, frame-rate goal, and stress case. A development workstation is not the minimum specification. Native handheld PCs may have tighter power and memory limits than desktop towers.

Frame time is `1000 / FPS` milliseconds. Leave headroom for CPU simulation, physics, rendering submission, GPU work, and loading spikes. Monitor frame-time distribution and recurring stutter as well as average FPS when profiling is authorized.

Keep costs distinct: active nodes and scripts, physics bodies/queries, visible geometry, material surfaces, draw calls and passes, lights/shadows, transparency coverage, texture residency, skinning, and resource loading. Package size and triangle counts do not establish runtime memory or performance.

## Renderer and quality choices

In Godot 4, Forward+ supplies the broadest 3D feature set; Mobile can suit simpler native desktop scenes; Compatibility can reach hardware without RenderingDevice support. Check driver availability, renderer fallback, and feature behavior for the target release/platform. [Renderer comparison](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).

Make quality settings a coherent profile with an understood cost: internal render scale, antialiasing, shadows, lighting, effects, textures, and LOD. Keep essential gameplay cues readable at lower settings. Do not present a feature as universally available across renderers.

Choose VSync, frame caps, and display modes from the latency, tearing, refresh-rate, and power goals. Preserve fixed simulation behavior as render frame rate changes; raising the physics tick rate is a separate CPU/behavior decision. Keep unsupported combinations and fallback behavior explicit.

## Visibility, repetition, and detail

Use the engine's frustum culling before adding custom visibility logic. Organize large environments into practical spatial chunks with accurate bounds. One merged level can inhibit culling; one node per small decorative detail can create unnecessary CPU work.

Use occlusion when opaque geometry materially blocks the camera view. Top-down views may offer little benefit. Chunk visibility must preserve what windows, doors, camera overlap, or roof cutaways expose, and it must not change approved game visibility rules.

Reuse meshes and material resources. Godot's automatic instancing is renderer-dependent; manual MultiMesh remains a deliberate option for repeated geometry. Split MultiMeshes by practical spatial bounds and retain gameplay identity outside render indices. [3D optimization](https://docs.godotengine.org/en/stable/tutorials/performance/optimizing_3d_performance.html), [MultiMesh](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html).

Use imported mesh LOD or visibility ranges when projected detail warrants it. A fixed orthographic camera can make distance thresholds less useful. Keep transitions stable and avoid rendering all manual LODs simultaneously. Reduce cosmetic animation/effects independently of authoritative state.

Budget runtime lighting and shadows for the visible scene. Baked lighting works for stable geometry but cannot automatically track moving doors, power changes, destruction, or lamps. Choose a documented static/dynamic split. High-end hardware does not make every GI, reflection, volumetric, or fullscreen effect free.

## Loading and CPU work

Streaming needs a traversal plan, memory budget, and defined reattachment behavior. Keep needed state, collision, and navigation available at boundaries. Background resource loading does not make arbitrary scene-tree changes thread-safe; use supported loading/threading APIs for the selected release.

Pool objects only where repeated creation/destruction causes a demonstrated cost. Reset signals, timers, ownership, and state before reuse. Retained pools can increase memory use.

Keep persistent simulation separate from rendering. If detailed offscreen AI/physics is reduced, define a coarser model that preserves prerequisites and consequences. Read [architecture and state](architecture-and-state.md) before changing job update behavior.

## Evidence when profiling is permitted

Compare equivalent camera views, population, quality, resolution, hardware, and build mode. Distinguish CPU and GPU bottlenecks and loading hitches before changing assets. Editor/debug instrumentation and release behavior may differ.

Use documented profiler/Performance monitors with their units and availability. Some monitors are debug-only. State whether results are measured, estimated, or unavailable. A Windows run does not prove Linux/macOS behavior, and an integrated GPU result does not establish every driver configuration. [Performance API](https://docs.godotengine.org/en/stable/classes/class_performance.html), [GPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html).
