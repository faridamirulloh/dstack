# Mobile performance

## Sustained budgets

Record low-end and representative target devices, OS, GPU/driver, renderer, internal resolution, frame-rate goal, visible populations, memory/startup goals, and performance duration. A short cold-device run can differ from sustained play as heat and power limits change.

Frame time is `1000 / FPS` milliseconds. Leave headroom for gameplay, physics, rendering, input, and loading. Keep the simulation's required cadence separate from the render cap. Favor a sustainable target over a peak number that requires excessive battery or thermal load.

When profiling is permitted, record device, build mode, resolution, scene/camera, quality, power state, and run duration. Distinguish CPU, GPU, memory, loading, and throttling effects. An emulator or desktop editor cannot establish physical-device performance.

## Renderer and feature selection

Godot 4 Mobile targets modern native devices with a reduced rendering feature set; Compatibility reaches devices without the necessary RenderingDevice support. Forward+ is supported on some mobile configurations but is poorly suited as a broad mobile default. Verify actual release/device features and driver behavior. [Renderer comparison](https://docs.godotengine.org/en/stable/tutorials/rendering/renderers.html).

Keep essential visuals available on the chosen baseline. Add quality profiles only where they have an understood cost and preserve gameplay cues. Renderer names are not performance guarantees; compare the actual scene when permitted.

## Pixel, geometry, and texture cost

Budget overlapping transparency, shaded pixels, texture reads, material passes, fullscreen effects, shadows, visible geometry, and skinning. Small high-detail objects can concentrate unnecessary work. Reducing triangles alone does not solve every GPU bottleneck. [GPU optimization](https://docs.godotengine.org/en/stable/tutorials/performance/gpu_optimization.html).

Use internal resolution scaling deliberately while keeping touch UI/text suitably sharp. Account for the device's native display density and aspect ratio. Antialiasing and expensive postprocessing need target-renderer support and measured cost.

Reuse meshes/materials and keep static geometry in practical chunks. MultiMesh can help repetition but is culled as a batch; split by spatial bounds. Do not expect Forward+ automatic instancing behavior in Mobile or Compatibility. [MultiMesh](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html), [3D optimization](https://docs.godotengine.org/en/stable/tutorials/performance/optimizing_3d_performance.html).

Allocate geometry to silhouette, moving parts, and interaction boundaries. Put suitable small surface detail into reusable or baked maps, balancing the texture/shader cost. Judge LOD by projected size and actual camera behavior; a fixed orthographic view can limit distance-based benefit.

Use texture formats/compression supported by the target release, renderer, and device tier. Compare artifact quality and memory/bandwidth costs. File compression, GPU compression, mipmaps, and residency are different things; a small downloaded texture file may still consume substantial memory after decoding.

## Lighting, animation, and CPU work

Use a deliberate static/dynamic lighting split. Baked lighting needs suitable UV2 and cannot automatically reflect changed power, moving doors, destruction, or lamps. Limit dynamic shadow coverage and overlapping lights according to the actual visible scene.

Budget visible skeletal actors, bones, blend shapes, animation update frequency, and particle coverage. Reduce cosmetic work independently of job/repair/energy simulation. A hidden model does not authorize stopping required gameplay progression.

Choose simple colliders and scope expensive physics/navigation queries. Lower-frequency offscreen simulation needs an explicit model that preserves prerequisites and consequences. Use pools only where churn is a demonstrated cost and reset all reused state.

## Memory, loading, and background behavior

Budget decoded textures/audio, geometry, state, temporary loading allocations, and simultaneously resident chunks. Streaming can briefly require old and new resources together. Culling hides geometry but does not free its resources.

Plan chunk boundaries and reattachment from traversal and persistent state. Avoid loading spikes during touch-critical actions where an agreed preload/loading flow is available. Use supported asynchronous-loading APIs without treating arbitrary scene-tree mutation as thread-safe.

Follow the approved pause/suspend policy. Native mobile backgrounding is not a request to keep rendering or add an OS background service. Resume should restore state and necessary resource/device connections without silently advancing game time.

## Evidence and settings

Change a specific quality or workload setting to address an observed cost. Compare equivalent scenes and record sustained results when profiling is authorized. Use documented monitor units and distinguish debug instrumentation from release behavior.

State which device tiers and lifecycle paths remain unverified. Do not turn a desktop result, asset triangle limit, or successful native export into a claim of acceptable sustained mobile performance.
