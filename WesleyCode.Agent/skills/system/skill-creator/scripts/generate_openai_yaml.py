#!/usr/bin/env python3
"""
生成 skill 的 `agents/openai.yaml`。

用法：
    generate_openai_yaml.py <skill_dir> [--name <skill_name>] [--interface key=value]
"""

import argparse
import re
import sys
from pathlib import Path

ACRONYMS = {
    "GH",
    "MCP",
    "API",
    "CI",
    "CLI",
    "LLM",
    "PDF",
    "PR",
    "UI",
    "URL",
    "SQL",
}

BRANDS = {
    "openai": "OpenAI",
    "openapi": "OpenAPI",
    "github": "GitHub",
    "pagerduty": "PagerDuty",
    "datadog": "DataDog",
    "sqlite": "SQLite",
    "fastapi": "FastAPI",
}

SMALL_WORDS = {"and", "or", "to", "up", "with"}

ALLOWED_INTERFACE_KEYS = {
    "display_name",
    "short_description",
    "icon_small",
    "icon_large",
    "brand_color",
    "default_prompt",
}

MIN_SHORT_DESCRIPTION_LENGTH = 8
MAX_SHORT_DESCRIPTION_LENGTH = 64


def yaml_quote(value):
    escaped = value.replace("\\", "\\\\").replace('"', '\\"').replace("\n", "\\n")
    return f'"{escaped}"'


def format_display_name(skill_name):
    words = [word for word in skill_name.split("-") if word]
    formatted = []
    for index, word in enumerate(words):
        lower = word.lower()
        upper = word.upper()
        if upper in ACRONYMS:
            formatted.append(upper)
            continue
        if lower in BRANDS:
            formatted.append(BRANDS[lower])
            continue
        if index > 0 and lower in SMALL_WORDS:
            formatted.append(lower)
            continue
        formatted.append(word.capitalize())
    return " ".join(formatted)


def generate_short_description(display_name):
    candidates = [
        f"{display_name}相关任务支持",
        f"用于{display_name}的技能支持",
        f"{display_name}技能工具",
        f"{display_name}助手",
    ]
    for description in candidates:
        if MIN_SHORT_DESCRIPTION_LENGTH <= len(description) <= MAX_SHORT_DESCRIPTION_LENGTH:
            return description
    return candidates[-1][:MAX_SHORT_DESCRIPTION_LENGTH].rstrip()


def read_frontmatter_name(skill_dir):
    skill_md = Path(skill_dir) / "SKILL.md"
    if not skill_md.exists():
        print(f"[错误] 未找到 SKILL.md：{skill_dir}")
        return None
    content = skill_md.read_text(encoding="utf-8")
    match = re.match(r"^---\n(.*?)\n---", content, re.DOTALL)
    if not match:
        print("[错误] SKILL.md frontmatter 格式无效。")
        return None
    frontmatter_text = match.group(1)

    import yaml

    try:
        frontmatter = yaml.safe_load(frontmatter_text)
    except yaml.YAMLError as exc:
        print(f"[错误] YAML frontmatter 无效：{exc}")
        return None
    if not isinstance(frontmatter, dict):
        print("[错误] Frontmatter 必须是 YAML 字典。")
        return None
    name = frontmatter.get("name", "")
    if not isinstance(name, str) or not name.strip():
        print("[错误] Frontmatter 中的 'name' 缺失或无效。")
        return None
    return name.strip()


def parse_interface_overrides(raw_overrides):
    overrides = {}
    optional_order = []
    for item in raw_overrides:
        if "=" not in item:
            print(f"[错误] 接口参数 '{item}' 格式无效，应为 key=value。")
            return None, None
        key, value = item.split("=", 1)
        key = key.strip()
        value = value.strip()
        if not key:
            print(f"[错误] 接口参数 '{item}' 的 key 不能为空。")
            return None, None
        if key not in ALLOWED_INTERFACE_KEYS:
            allowed = ", ".join(sorted(ALLOWED_INTERFACE_KEYS))
            print(f"[错误] 未知接口字段 '{key}'。允许值：{allowed}")
            return None, None
        overrides[key] = value
        if key not in ("display_name", "short_description") and key not in optional_order:
            optional_order.append(key)
    return overrides, optional_order


def write_openai_yaml(skill_dir, skill_name, raw_overrides):
    overrides, optional_order = parse_interface_overrides(raw_overrides)
    if overrides is None:
        return None

    display_name = overrides.get("display_name") or format_display_name(skill_name)
    short_description = overrides.get("short_description") or generate_short_description(display_name)

    if not (MIN_SHORT_DESCRIPTION_LENGTH <= len(short_description) <= MAX_SHORT_DESCRIPTION_LENGTH):
        print(
            f"[错误] short_description 长度必须在 {MIN_SHORT_DESCRIPTION_LENGTH}-{MAX_SHORT_DESCRIPTION_LENGTH} 个字符之间"
            f"（当前 {len(short_description)}）。"
        )
        return None

    interface_lines = [
        "interface:",
        f"  display_name: {yaml_quote(display_name)}",
        f"  short_description: {yaml_quote(short_description)}",
    ]

    for key in optional_order:
        value = overrides.get(key)
        if value is not None:
            interface_lines.append(f"  {key}: {yaml_quote(value)}")

    agents_dir = Path(skill_dir) / "agents"
    agents_dir.mkdir(parents=True, exist_ok=True)
    output_path = agents_dir / "openai.yaml"
    output_path.write_text("\n".join(interface_lines) + "\n", encoding="utf-8")
    print("[完成] 已生成 agents/openai.yaml")
    return output_path


def main():
    parser = argparse.ArgumentParser(
        description="为 skill 目录生成 agents/openai.yaml。",
    )
    parser.add_argument("skill_dir", help="skill 目录路径")
    parser.add_argument(
        "--name",
        help="skill 名称覆盖值（默认读取 SKILL.md frontmatter）",
    )
    parser.add_argument(
        "--interface",
        action="append",
        default=[],
        help="以 key=value 形式覆盖 interface 字段，可重复传入",
    )
    args = parser.parse_args()

    skill_dir = Path(args.skill_dir).resolve()
    if not skill_dir.exists():
        print(f"[错误] skill 目录不存在：{skill_dir}")
        sys.exit(1)
    if not skill_dir.is_dir():
        print(f"[错误] 路径不是目录：{skill_dir}")
        sys.exit(1)

    skill_name = args.name or read_frontmatter_name(skill_dir)
    if not skill_name:
        sys.exit(1)

    result = write_openai_yaml(skill_dir, skill_name, args.interface)
    if result:
        sys.exit(0)
    sys.exit(1)


if __name__ == "__main__":
    main()
