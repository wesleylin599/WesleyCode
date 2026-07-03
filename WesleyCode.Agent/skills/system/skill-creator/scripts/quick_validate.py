#!/usr/bin/env python3
"""Skill 结构快速校验脚本（精简版）。"""

import re
import sys
from pathlib import Path

import yaml

MAX_SKILL_NAME_LENGTH = 64


def validate_skill(skill_path):
    """执行基础 skill 校验。"""
    skill_path = Path(skill_path)

    skill_md = skill_path / "SKILL.md"
    if not skill_md.exists():
        return False, "未找到 SKILL.md"

    content = skill_md.read_text(encoding="utf-8")
    if not content.startswith("---"):
        return False, "未找到 YAML frontmatter"

    match = re.match(r"^---\n(.*?)\n---", content, re.DOTALL)
    if not match:
        return False, "Frontmatter 格式无效"

    frontmatter_text = match.group(1)

    try:
        frontmatter = yaml.safe_load(frontmatter_text)
        if not isinstance(frontmatter, dict):
            return False, "Frontmatter 必须是 YAML 字典"
    except yaml.YAMLError as e:
        return False, f"Frontmatter 中存在无效 YAML：{e}"

    allowed_properties = {"name", "description", "license", "allowed-tools", "metadata"}

    unexpected_keys = set(frontmatter.keys()) - allowed_properties
    if unexpected_keys:
        allowed = ", ".join(sorted(allowed_properties))
        unexpected = ", ".join(sorted(unexpected_keys))
        return (
            False,
            f"SKILL.md frontmatter 中存在未允许字段：{unexpected}。允许字段：{allowed}",
        )

    if "name" not in frontmatter:
        return False, "Frontmatter 缺少 'name'"
    if "description" not in frontmatter:
        return False, "Frontmatter 缺少 'description'"

    name = frontmatter.get("name", "")
    if not isinstance(name, str):
        return False, f"name 必须是字符串，当前类型：{type(name).__name__}"
    name = name.strip()
    if name:
        if not re.match(r"^[a-z0-9-]+$", name):
            return (
                False,
                f"name '{name}' 必须使用短横线命名，仅允许小写字母、数字和连字符",
            )
        if name.startswith("-") or name.endswith("-") or "--" in name:
            return (
                False,
                f"name '{name}' 不能以连字符开头/结尾，也不能包含连续连字符",
            )
        if len(name) > MAX_SKILL_NAME_LENGTH:
            return (
                False,
                f"name 过长（{len(name)} 个字符），最大允许 {MAX_SKILL_NAME_LENGTH} 个字符。",
            )

    description = frontmatter.get("description", "")
    if not isinstance(description, str):
        return False, f"description 必须是字符串，当前类型：{type(description).__name__}"
    description = description.strip()
    if description:
        if "<" in description or ">" in description:
            return False, "description 不能包含尖括号（< 或 >）"
        if len(description) > 1024:
            return (
                False,
                f"description 过长（{len(description)} 个字符），最大允许 1024 个字符。",
            )

    return True, "Skill 结构有效。"


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("用法：python quick_validate.py <skill_directory>")
        sys.exit(1)

    valid, message = validate_skill(sys.argv[1])
    print(message)
    sys.exit(0 if valid else 1)
