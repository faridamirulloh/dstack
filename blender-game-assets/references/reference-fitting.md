# Reference fitting

Use this guidance for an approved blueprint, image-based reconstruction, or fitting task. Ordinary modeling from a short brief does not require a registration, landmark, or multiview optimization system.

## Establish what the reference proves

Identify the authoritative image/view, known dimensions, perspective or orthographic projection, visible parts, and which details are uncertain. A front silhouette cannot establish depth, hidden surfaces, internal mechanics, or every part's function. Ask about missing information only when it changes the requested outcome.

Use source fidelity when requested; do not force an industrial prop into a mascot/logo workflow. Preserve exact colors, landmarks, or part boundaries only when those are requirements. Agree on any simplification needed for the runtime camera or budget.

For multiple views, check that they depict the same asset, scale, pose, and camera conventions. Conflicting views need an authority or a documented interpretation. Align images before treating pixel differences as geometry errors.

## Fit geometry before material complexity

Block out the major volumes and register the relevant camera. Compare silhouette, proportions, spacing, and attachment locations from the authoritative view. Add depth from side/top evidence or agreed assumptions.

Use a small set of meaningful landmarks rather than trying to match every image pixel. A camera change can disguise wrong geometry; keep projection and camera parameters stable once established unless evidence shows registration is wrong.

Preserve semantic parts independently of the final mesh batching. One visual region need not become exactly one mesh, and a hidden assembly should not be invented merely to satisfy a part-count rule.

## Contours and texture alignment

Contour extrusion is useful for flat signs, plaques, reliefs, or other intentionally shallow assets. It is not a general multiview reconstruction method. Preserve holes and separate islands when they are part of the source.

For triangulation, choose a method that respects boundaries and holes. Keeping a triangle because its centroid lies inside a contour does not guarantee its edges stay inside a concave shape. Do not reuse the reviewed helper's largest-external-contour approach when holes or disconnected pieces matter.

Fit UV landmarks and atlas regions after geometry and camera alignment are sufficiently stable. Preserve sides/backs and texture margins; a good front match can still leave stretched or untextured unseen surfaces that become visible in gameplay.

Calibrate lighting/color separately from geometry. Do not compensate for the wrong shape with baked shadows or a heavily altered camera. State when the output aims for source-image resemblance versus physically consistent material appearance.

## Bounded repair

Pick the current largest mismatch, change its likely cause, and compare again under the same conditions when rendering is authorized. Stop when the agreed fidelity/runtime goal is met, the remaining error requires missing evidence, or the allowed work budget is reached.

Keep fitting measurements tied to their inspected objects and views. An empty object set, invalid camera, or missing reference cannot produce a passing comparison. Distinguish measured errors from visual estimates.

Do not automatically modify this skill, write a release report, or launch a repository audit because one asset needed repair. Make skill improvements only when requested or supported by the task's scope.

Report known dimensions, reference authority, inferred depth/hidden geometry, simplifications, and remaining mismatches. The result should make its uncertainty visible rather than implying the reference established information it did not contain.
