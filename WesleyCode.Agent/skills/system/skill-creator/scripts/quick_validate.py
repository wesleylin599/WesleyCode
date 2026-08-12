#!/usr/bin/env python3
"""Quick skill structure validation script (minimal version)."""

import re
import sys
from pathlib import Path

import yaml

MAX_SKILL_NAME_LENGTH = 64


def validate_skill(skill_path):
    """Run basic skill validation."""
    skill_path = Path(skill_path)

    skill_md = skill_path / "SKILL.md"
    if not skill_md.exists():
        return False, "SKILL.md not found"

    content = skill_md.read_text(encoding="utf-8")
    if not content.startswith("---"):
        return False, "YAML frontmatter not found"

    match = re.match(r"^---\n(.*?)\n---", content, re.DOTALL)
    if not match:
        return False, "Invalid frontmatter format"

    frontmatter_text = match.group(1)

    try:
        frontmatter = yaml.safe_load(frontmatter_text)
        if not isinstance(frontmatter, dict):
            return False, "Frontmatter must be a YAML mapping"
    except yaml.YAMLError as e:
        return False, f"Frontmatter contains invalid YAML: {e}"

    allowed_properties = {"name", "description", "license", "allowed-tools", "metadata"}

    unexpected_keys = set(frontmatter.keys()) - allowed_properties
    if unexpected_keys:
        allowed = ", ".join(sorted(allowed_properties))
        unexpected = ", ".join(sorted(unexpected_keys))
        return (
            False,
            f"SKILL.md frontmatter contains unsupported fields: {unexpected}. Allowed fields: {allowed}",
        )

    if "name" not in frontmatter:
        return False, "Frontmatter is missing 'name'"
    if "description" not in frontmatter:
        return False, "Frontmatter is missing 'description'"

    name = frontmatter.get("name", "")
    if not isinstance(name, str):
        return False, f"name must be a string; current type: {type(name).__name__}"
    name = name.strip()
    if name:
        if not re.match(r"^[a-z0-9-]+$", name):
            return (
                False,
                f"name '{name}' must use kebab-case and only lowercase letters, digits, and hyphens",
            )
        if name.startswith("-") or name.endswith("-") or "--" in name:
            return (
                False,
                f"name '{name}' must not start or end with a hyphen or contain consecutive hyphens",
            )
        if len(name) > MAX_SKILL_NAME_LENGTH:
            return (
                False,
                f"name is too long ({len(name)} characters); maximum allowed length is {MAX_SKILL_NAME_LENGTH} characters.",
            )

    description = frontmatter.get("description", "")
    if not isinstance(description, str):
        return False, f"description must be a string; current type: {type(description).__name__}"
    description = description.strip()
    if description:
        if "<" in description or ">" in description:
            return False, "description must not contain angle brackets (< or >)"
        if len(description) > 1024:
            return (
                False,
                f"description is too long ({len(description)} characters); maximum allowed length is 1024 characters.",
            )

    return True, "Skill structure is valid."


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("Usage: python quick_validate.py <skill_directory>")
        sys.exit(1)

    valid, message = validate_skill(sys.argv[1])
    print(message)
    sys.exit(0 if valid else 1)
