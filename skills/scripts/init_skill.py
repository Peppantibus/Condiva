#!/usr/bin/env python
"""Initialize a Codex skill directory with SKILL.md and optional resources."""

import argparse
from pathlib import Path


def normalize_name(raw: str) -> str:
    name = raw.strip().lower().replace(" ", "-").replace("_", "-")
    if not name:
        raise ValueError("Skill name is required.")
    for ch in name:
        if not ("a" <= ch <= "z" or "0" <= ch <= "9" or ch == "-"):
            raise ValueError(
                "Skill name must contain only lowercase letters, digits, and hyphens."
            )
    return name


def parse_resources(value: str) -> list[str]:
    if not value:
        return []
    parts = [p.strip().lower() for p in value.split(",") if p.strip()]
    allowed = {"scripts", "references", "assets"}
    unknown = [p for p in parts if p not in allowed]
    if unknown:
        raise ValueError(f"Unknown resources: {', '.join(unknown)}")
    return parts


def write_skill_md(path: Path, name: str) -> None:
    content = f"""---
name: {name}
description: \"TODO: Describe what this skill does and when to use it.\"
---

# {name}

## Goal

- TODO: State the goal in one sentence.

## Workflow

1) TODO: Step one
2) TODO: Step two
3) TODO: Step three

## Notes

- TODO: Add any constraints or important details.
"""
    path.write_text(content, encoding="utf-8")


def write_examples(resource_dirs: list[Path]) -> None:
    for resource_dir in resource_dirs:
        example = resource_dir / "example.txt"
        example.write_text(
            "TODO: Replace this placeholder with a real example file.\n",
            encoding="utf-8",
        )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Initialize a skill folder with SKILL.md and optional resources."
    )
    parser.add_argument("skill_name", help="Skill name (lowercase letters, digits, hyphens)")
    parser.add_argument(
        "--path",
        required=True,
        help="Output directory that will contain the skill folder",
    )
    parser.add_argument(
        "--resources",
        default="",
        help="Comma-separated list: scripts,references,assets",
    )
    parser.add_argument(
        "--examples",
        action="store_true",
        help="Create placeholder example files in resource directories",
    )
    args = parser.parse_args()

    name = normalize_name(args.skill_name)
    base = Path(args.path).expanduser().resolve()
    skill_dir = base / name

    if skill_dir.exists():
        raise SystemExit(f"Skill directory already exists: {skill_dir}")

    skill_dir.mkdir(parents=True, exist_ok=False)
    write_skill_md(skill_dir / "SKILL.md", name)

    resource_names = parse_resources(args.resources)
    resource_dirs = []
    for resource in resource_names:
        resource_dir = skill_dir / resource
        resource_dir.mkdir(parents=True, exist_ok=False)
        resource_dirs.append(resource_dir)

    if args.examples and resource_dirs:
        write_examples(resource_dirs)

    print(f"Created skill at {skill_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
