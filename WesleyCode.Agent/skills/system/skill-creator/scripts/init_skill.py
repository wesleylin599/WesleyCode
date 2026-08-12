#!/usr/bin/env python3
"""
Skill initializer: create a new skill from templates.

Usage:
    init_skill.py <skill-name> --path <path> [--resources scripts,references,assets] [--examples] [--interface key=value]
"""

import argparse
import os
import re
import sys
from pathlib import Path

from generate_openai_yaml import write_openai_yaml

MAX_SKILL_NAME_LENGTH = 64
ALLOWED_RESOURCES = {"scripts", "references", "assets"}

SKILL_TEMPLATE = """---
name: {skill_name}
description: "TODO: Clearly describe what this skill does and when it should be triggered. Mention typical tasks, file types, or context clues."
---

# {skill_title}

## Overview

[TODO: Describe the scope of this skill in 1-2 sentences.]

## How To Organize This Skill

[TODO: Choose the structure that best fits the actual use case:

1. Workflow: best for tasks with clear ordered steps
2. Task-oriented: best for a collection of tools or capabilities
3. Standards-oriented: best for rules, standards, or requirements
4. Capability-oriented: best for multiple related sub-capabilities

Remove this guidance section when done.]

## [TODO: Replace with your first real section title]

[TODO: Add core instructions here. You may include:
- Key steps
- Decision branches
- Typical examples
- References to scripts/references/assets]

## Resources (Optional)

Keep only the resource directories this skill actually needs; remove this section if unnecessary.

### scripts/
Place directly executable scripts here for automation, data processing, file conversion, and other deterministic tasks.

### references/
Place detailed references here, such as API docs, database schemas, business rules, and workflow notes.

### assets/
Place templates, images, fonts, starter projects, or other assets used for outputs here.

---

**Not every skill needs all three resource types.**
"""

EXAMPLE_SCRIPT = '''#!/usr/bin/env python3
"""
Example script for {skill_name}

This is a placeholder script. Replace or remove it as needed.
"""


def main():
    print("This is an example script for {skill_name}")


if __name__ == "__main__":
    main()
'''

EXAMPLE_REFERENCE = """# {skill_title} Reference

This is a placeholder reference document. Replace or remove it as needed.

You can place the following here:
- API documentation summaries
- Data structure notes
- Detailed workflow notes
- Complex rules or standards
"""

EXAMPLE_ASSET = """Example asset file

This is a placeholder file that reminds you to place real assets in `assets/`.
Replace it with templates, icons, fonts, starter projects, sample data, or similar assets.
"""


def normalize_skill_name(skill_name):
    """Normalize a skill name to lowercase kebab-case."""
    normalized = skill_name.strip().lower()
    normalized = re.sub(r"[^a-z0-9]+", "-", normalized)
    normalized = normalized.strip("-")
    normalized = re.sub(r"-{2,}", "-", normalized)
    return normalized


def title_case_skill_name(skill_name):
    """Convert a kebab-case skill name to title case."""
    return " ".join(word.capitalize() for word in skill_name.split("-"))


def parse_resources(raw_resources):
    if not raw_resources:
        return []
    resources = [item.strip() for item in raw_resources.split(",") if item.strip()]
    invalid = sorted({item for item in resources if item not in ALLOWED_RESOURCES})
    if invalid:
        allowed = ", ".join(sorted(ALLOWED_RESOURCES))
        print(f"[Error] Unknown resource type: {', '.join(invalid)}")
        print(f"   Allowed values: {allowed}")
        sys.exit(1)
    deduped = []
    seen = set()
    for resource in resources:
        if resource not in seen:
            deduped.append(resource)
            seen.add(resource)
    return deduped


def default_skills_root():
    configured = os.environ.get("WESLEY_SKILLS_ROOT")
    if configured:
        return Path(configured).resolve()

    for parent in Path(__file__).resolve().parents:
        if parent.name == "skills":
            return parent

    return (Path.cwd() / "skills").resolve()


def create_resource_dirs(skill_dir, skill_name, skill_title, resources, include_examples):
    for resource in resources:
        resource_dir = skill_dir / resource
        resource_dir.mkdir(exist_ok=True)
        if resource == "scripts":
            if include_examples:
                example_script = resource_dir / "example.py"
                example_script.write_text(EXAMPLE_SCRIPT.format(skill_name=skill_name), encoding="utf-8")
                example_script.chmod(0o755)
                print("[Done] Created scripts/example.py")
            else:
                print("[Done] Created scripts/")
        elif resource == "references":
            if include_examples:
                example_reference = resource_dir / "api_reference.md"
                example_reference.write_text(EXAMPLE_REFERENCE.format(skill_title=skill_title), encoding="utf-8")
                print("[Done] Created references/api_reference.md")
            else:
                print("[Done] Created references/")
        elif resource == "assets":
            if include_examples:
                example_asset = resource_dir / "example_asset.txt"
                example_asset.write_text(EXAMPLE_ASSET, encoding="utf-8")
                print("[Done] Created assets/example_asset.txt")
            else:
                print("[Done] Created assets/")


def init_skill(skill_name, path, resources, include_examples, interface_overrides):
    skill_dir = Path(path).resolve() / skill_name

    if skill_dir.exists():
        print(f"[Error] Skill directory already exists: {skill_dir}")
        return None

    try:
        skill_dir.mkdir(parents=True, exist_ok=False)
        print(f"[Done] Created skill directory: {skill_dir}")
    except Exception as e:
        print(f"[Error] Failed to create directory: {e}")
        return None

    skill_title = title_case_skill_name(skill_name)
    skill_content = SKILL_TEMPLATE.format(skill_name=skill_name, skill_title=skill_title)

    skill_md_path = skill_dir / "SKILL.md"
    try:
        skill_md_path.write_text(skill_content, encoding="utf-8")
        print("[Done] Created SKILL.md")
    except Exception as e:
        print(f"[Error] Failed to create SKILL.md: {e}")
        return None

    try:
        result = write_openai_yaml(skill_dir, skill_name, interface_overrides)
        if not result:
            return None
    except Exception as e:
        print(f"[Error] Failed to create agents/openai.yaml: {e}")
        return None

    if resources:
        try:
            create_resource_dirs(skill_dir, skill_name, skill_title, resources, include_examples)
        except Exception as e:
            print(f"[Error] Failed to create resource directories: {e}")
            return None

    print(f"\n[Done] Initialized skill '{skill_name}': {skill_dir}")
    print("\nNext steps:")
    print("1. Edit SKILL.md, complete the TODOs, and refine the trigger description")
    if resources:
        if include_examples:
            print("2. Replace or remove example files under scripts/, references/, and assets/ as needed")
        else:
            print("2. Add real resources under scripts/, references/, and assets/ as needed")
    else:
        print("2. Create scripts/, references/, and assets/ only when they are actually needed")
    print("3. Update agents/openai.yaml if interface text needs adjustment")
    print("4. Run the validation script after finishing to check the skill structure")
    print("5. Test complex skills with realistic requests")

    return skill_dir


def main():
    parser = argparse.ArgumentParser(
        description="Create a new skill directory from templates.",
    )
    parser.add_argument("skill_name", help="Skill name (automatically normalized to kebab-case)")
    parser.add_argument("--path", help="Skill output directory; defaults to the current WesleyCode skills directory")
    parser.add_argument(
        "--resources",
        default="",
        help="Comma-separated resource directories: scripts,references,assets",
    )
    parser.add_argument(
        "--examples",
        action="store_true",
        help="Create example files in the selected resource directories",
    )
    parser.add_argument(
        "--interface",
        action="append",
        default=[],
        help="Override interface fields as key=value; can be repeated",
    )
    args = parser.parse_args()

    raw_skill_name = args.skill_name
    skill_name = normalize_skill_name(raw_skill_name)
    if not skill_name:
        print("[Error] Skill name must contain at least one letter or digit.")
        sys.exit(1)
    if len(skill_name) > MAX_SKILL_NAME_LENGTH:
        print(
            f"[Error] Skill name '{skill_name}' is too long ({len(skill_name)} characters); "
            f"maximum allowed length is {MAX_SKILL_NAME_LENGTH} characters."
        )
        sys.exit(1)
    if skill_name != raw_skill_name:
        print(f"Note: normalized skill name from '{raw_skill_name}' to '{skill_name}'.")

    resources = parse_resources(args.resources)
    if args.examples and not resources:
        print("[Error] --resources is required when using --examples.")
        sys.exit(1)

    path = args.path or str(default_skills_root())

    print(f"Initializing skill: {skill_name}")
    print(f"   Location: {path}")
    if resources:
        print(f"   Resources: {', '.join(resources)}")
        if args.examples:
            print("   Example files: enabled")
    else:
        print("   Resources: none (create as needed)")
    print()

    result = init_skill(skill_name, path, resources, args.examples, args.interface)

    if result:
        sys.exit(0)
    sys.exit(1)


if __name__ == "__main__":
    main()
