#!/usr/bin/env python3
"""Validate skill routing and local references with the Python standard library."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlparse


PLUGIN_ROOT = Path(__file__).resolve().parents[1]
SKILLS_ROOT = PLUGIN_ROOT / "skills"
ROUTER = "engineering-router"
REMOVED_ENTRYPOINTS = {
    "blast-radius",
    "codebase-design",
    "figure-it-out",
    "principle-model-the-domain",
    "why",
    "writing-for-agents",
}
EXPECTED_SKILLS = {
    "architect",
    "bro",
    "code-review",
    "domain-modeling",
    "engineering-router",
    "grill-me",
    "grill-with-docs",
    "grilling",
    "handoff",
    "how",
    "improve-codebase-architecture",
    "no-comments",
    "principle-boundary-discipline",
    "principle-build-the-lever",
    "principle-encode-lessons-in-structure",
    "principle-exhaust-the-design-space",
    "principle-experience-first",
    "principle-fix-root-causes",
    "principle-foundational-thinking",
    "principle-guard-the-context-window",
    "principle-laziness-protocol",
    "principle-make-operations-idempotent",
    "principle-migrate-callers-then-delete-legacy-apis",
    "principle-minimize-reader-load",
    "principle-never-block-on-the-human",
    "principle-outcome-oriented-execution",
    "principle-prove-it-works",
    "principle-redesign-from-first-principles",
    "principle-separate-before-serializing-shared-state",
    "principle-sequence-verifiable-units",
    "principle-subtract-before-you-add",
    "principle-type-system-discipline",
    "prototype",
    "research",
    "resolving-merge-conflicts",
    "teach",
    "technical-writing",
    "to-questionnaire",
    "typescript-best-practices",
    "unslop",
    "wizard",
}
EXPECTED_PLAYBOOKS = {
    "bug-fix.md",
    "feature.md",
    "investigation.md",
    "multi-phase-plan.md",
    "prototype.md",
    "refactoring.md",
}

FRONTMATTER = re.compile(r"\A---\r?\n(?P<body>.*?)\r?\n---(?:\r?\n|\Z)", re.DOTALL)
NAME = re.compile(r"(?m)^name:\s*[\"']?([a-z0-9-]+)[\"']?\s*$")
SKILL_REFERENCE = re.compile(r"(?<!\w)\$([a-z][a-z0-9-]*)")
MARKDOWN_LINK = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")
FENCED_CODE = re.compile(r"```.*?```", re.DOTALL)


def visible_markdown(text: str) -> str:
    return FENCED_CODE.sub("", text)


def frontmatter_name(path: Path, errors: list[str]) -> str | None:
    text = path.read_text(encoding="utf-8-sig")
    match = FRONTMATTER.match(text)
    if not match:
        errors.append(f"{path}: missing or malformed YAML frontmatter")
        return None
    name = NAME.search(match.group("body"))
    if not name:
        errors.append(f"{path}: missing valid frontmatter name")
        return None
    return name.group(1)


def invocation_policy(skill_dir: Path, errors: list[str]) -> bool | None:
    path = skill_dir / "agents" / "openai.yaml"
    if not path.is_file():
        errors.append(f"{path}: missing")
        return None
    text = path.read_text(encoding="utf-8-sig")
    for field in ("display_name", "short_description"):
        value = re.search(rf'(?m)^\s*{field}:\s*"([^"\r\n]+)"\s*$', text)
        if not value or any(token in value.group(1) for token in ("`$", " + ")):
            errors.append(f"{path}: invalid interface.{field}")
    match = re.search(
        r"(?m)^\s*allow_implicit_invocation:\s*(true|false)\s*$",
        text,
    )
    if not match:
        errors.append(f"{path}: missing allow_implicit_invocation policy")
        return None
    return match.group(1) == "true"


def check_link(source: Path, raw_target: str, errors: list[str]) -> bool:
    target = raw_target.strip().strip("<>").split(maxsplit=1)[0]
    parsed = urlparse(target)
    if parsed.scheme or target.startswith("#"):
        return False
    path_part = unquote(target.split("#", 1)[0])
    if not path_part:
        return False
    resolved = (source.parent / path_part).resolve()
    if not resolved.exists():
        errors.append(f"{source}: broken Markdown link {raw_target!r}")
        return True
    try:
        relative = resolved.relative_to(PLUGIN_ROOT.resolve())
    except ValueError:
        errors.append(f"{source}: local Markdown link leaves the plugin {raw_target!r}")
        return True
    current = PLUGIN_ROOT
    for part in relative.parts:
        exact_names = {child.name for child in current.iterdir()}
        if part not in exact_names:
            errors.append(f"{source}: Markdown link has incorrect path case {raw_target!r}")
            break
        current /= part
    return True


def main() -> int:
    errors: list[str] = []
    skill_dirs = sorted(
        path for path in SKILLS_ROOT.iterdir() if path.is_dir() and (path / "SKILL.md").is_file()
    )
    folder_names = {path.name for path in skill_dirs}

    missing = EXPECTED_SKILLS - folder_names
    unexpected = folder_names - EXPECTED_SKILLS
    if missing:
        errors.append(f"missing skills: {', '.join(sorted(missing))}")
    if unexpected:
        errors.append(f"unexpected skills: {', '.join(sorted(unexpected))}")

    declared: dict[str, Path] = {}
    for skill_dir in skill_dirs:
        name = frontmatter_name(skill_dir / "SKILL.md", errors)
        if name is None:
            continue
        if name != skill_dir.name:
            errors.append(f"{skill_dir}: folder name does not match frontmatter {name!r}")
        if name in declared:
            errors.append(f"duplicate skill name {name!r}: {declared[name]} and {skill_dir}")
        declared[name] = skill_dir

        implicit = invocation_policy(skill_dir, errors)
        expected_implicit = name == ROUTER
        if implicit is not None and implicit != expected_implicit:
            errors.append(
                f"{skill_dir / 'agents' / 'openai.yaml'}: implicit invocation must be "
                f"{str(expected_implicit).lower()}"
            )

    playbooks = {
        path.name
        for path in (SKILLS_ROOT / ROUTER / "references" / "playbooks").glob("*.md")
    }
    if playbooks != EXPECTED_PLAYBOOKS:
        errors.append(
            "router playbooks differ: "
            f"missing={sorted(EXPECTED_PLAYBOOKS - playbooks)}, "
            f"unexpected={sorted(playbooks - EXPECTED_PLAYBOOKS)}"
        )

    reference_count = 0
    link_count = 0
    slash_stale = re.compile(
        r"(?<![\w/])/(?:" + "|".join(map(re.escape, REMOVED_ENTRYPOINTS)) + r")(?![\w-])"
    )
    for path in PLUGIN_ROOT.rglob("*.md"):
        text = visible_markdown(path.read_text(encoding="utf-8-sig"))
        for referenced in SKILL_REFERENCE.findall(text):
            reference_count += 1
            if referenced not in declared:
                errors.append(f"{path}: unresolved skill reference ${referenced}")
            if referenced in REMOVED_ENTRYPOINTS:
                errors.append(f"{path}: stale removed skill reference ${referenced}")
        for stale in slash_stale.finditer(text):
            errors.append(f"{path}: stale slash invocation {stale.group(0)}")
        for target in MARKDOWN_LINK.findall(text):
            link_count += int(check_link(path, target, errors))

    if errors:
        print("route check failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print(
        f"route check passed: {len(declared)} skills, {len(playbooks)} playbooks, "
        f"{reference_count} skill references, {link_count} local links"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
