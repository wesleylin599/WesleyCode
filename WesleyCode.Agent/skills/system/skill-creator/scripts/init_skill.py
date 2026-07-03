#!/usr/bin/env python3
"""
Skill 初始化器：根据模板创建新的 skill。

用法：
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
description: "TODO: 用清晰完整的语言说明这个 skill 能做什么，以及应在什么场景下触发。请写明典型任务、文件类型或上下文线索。"
---

# {skill_title}

## 概述

[TODO: 用 1-2 句话说明这个 skill 的能力范围。]

## 如何组织这个 Skill

[TODO: 按实际用途选择最合适的结构：

1. 流程型：适合有明确先后步骤的任务
2. 任务型：适合多个工具或能力集合
3. 规范型：适合规则、标准、要求说明
4. 能力型：适合多个互相关联的子能力

完成后删除本节提示内容。]

## [TODO: 替换为你的第一个正式章节标题]

[TODO: 在这里补充核心说明，可包含：
- 关键步骤
- 判断分支
- 典型示例
- 对 scripts/references/assets 的引用]

## 资源（可选）

只保留当前 skill 真正需要的资源目录；不需要时删除本节。

### scripts/
放可直接执行的脚本，用于自动化、数据处理、文件转换等确定性任务。

### references/
放详细参考资料，例如 API 文档、数据库结构、业务规则、流程说明。

### assets/
放模板、图片、字体、样板工程或其他输出要用到的素材文件。

---

**不是每个 skill 都需要这三类资源。**
"""

EXAMPLE_SCRIPT = '''#!/usr/bin/env python3
"""
{skill_name} 的示例脚本

这是一个占位脚本，请按实际需要替换或删除。
"""


def main():
    print("这是 {skill_name} 的示例脚本")


if __name__ == "__main__":
    main()
'''

EXAMPLE_REFERENCE = """# {skill_title} 参考资料

这是一个占位参考文档，请按实际需要替换或删除。

你可以在这里放：
- API 文档摘要
- 数据结构说明
- 详细流程说明
- 复杂规则或规范
"""

EXAMPLE_ASSET = """示例素材文件

这是占位文件，用于提示你把真实素材放在 `assets/` 目录中。
可替换为模板、图标、字体、样板工程、示例数据等。
"""


def normalize_skill_name(skill_name):
    """把 skill 名称规范化为小写短横线形式。"""
    normalized = skill_name.strip().lower()
    normalized = re.sub(r"[^a-z0-9]+", "-", normalized)
    normalized = normalized.strip("-")
    normalized = re.sub(r"-{2,}", "-", normalized)
    return normalized


def title_case_skill_name(skill_name):
    """把短横线命名转换为标题形式。"""
    return " ".join(word.capitalize() for word in skill_name.split("-"))


def parse_resources(raw_resources):
    if not raw_resources:
        return []
    resources = [item.strip() for item in raw_resources.split(",") if item.strip()]
    invalid = sorted({item for item in resources if item not in ALLOWED_RESOURCES})
    if invalid:
        allowed = ", ".join(sorted(ALLOWED_RESOURCES))
        print(f"[错误] 未知资源类型：{', '.join(invalid)}")
        print(f"   允许值：{allowed}")
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
                print("[完成] 已创建 scripts/example.py")
            else:
                print("[完成] 已创建 scripts/")
        elif resource == "references":
            if include_examples:
                example_reference = resource_dir / "api_reference.md"
                example_reference.write_text(EXAMPLE_REFERENCE.format(skill_title=skill_title), encoding="utf-8")
                print("[完成] 已创建 references/api_reference.md")
            else:
                print("[完成] 已创建 references/")
        elif resource == "assets":
            if include_examples:
                example_asset = resource_dir / "example_asset.txt"
                example_asset.write_text(EXAMPLE_ASSET, encoding="utf-8")
                print("[完成] 已创建 assets/example_asset.txt")
            else:
                print("[完成] 已创建 assets/")


def init_skill(skill_name, path, resources, include_examples, interface_overrides):
    skill_dir = Path(path).resolve() / skill_name

    if skill_dir.exists():
        print(f"[错误] skill 目录已存在：{skill_dir}")
        return None

    try:
        skill_dir.mkdir(parents=True, exist_ok=False)
        print(f"[完成] 已创建 skill 目录：{skill_dir}")
    except Exception as e:
        print(f"[错误] 创建目录失败：{e}")
        return None

    skill_title = title_case_skill_name(skill_name)
    skill_content = SKILL_TEMPLATE.format(skill_name=skill_name, skill_title=skill_title)

    skill_md_path = skill_dir / "SKILL.md"
    try:
        skill_md_path.write_text(skill_content, encoding="utf-8")
        print("[完成] 已创建 SKILL.md")
    except Exception as e:
        print(f"[错误] 创建 SKILL.md 失败：{e}")
        return None

    try:
        result = write_openai_yaml(skill_dir, skill_name, interface_overrides)
        if not result:
            return None
    except Exception as e:
        print(f"[错误] 创建 agents/openai.yaml 失败：{e}")
        return None

    if resources:
        try:
            create_resource_dirs(skill_dir, skill_name, skill_title, resources, include_examples)
        except Exception as e:
            print(f"[错误] 创建资源目录失败：{e}")
            return None

    print(f"\n[完成] Skill '{skill_name}' 已初始化：{skill_dir}")
    print("\n后续建议：")
    print("1. 编辑 SKILL.md，补全 TODO 并完善触发描述")
    if resources:
        if include_examples:
            print("2. 根据需要替换或删除 scripts/、references/、assets/ 下的示例文件")
        else:
            print("2. 按需向 scripts/、references/、assets/ 添加真实资源")
    else:
        print("2. 仅在确实需要时再创建 scripts/、references/、assets/")
    print("3. 如果界面文案需要调整，更新 agents/openai.yaml")
    print("4. 完成后运行校验脚本检查 skill 结构")
    print("5. 对复杂 skill 用真实请求做前向测试")

    return skill_dir


def main():
    parser = argparse.ArgumentParser(
        description="根据模板创建新的 skill 目录。",
    )
    parser.add_argument("skill_name", help="skill 名称（会自动规范化为短横线格式）")
    parser.add_argument("--path", help="skill 输出目录，默认自动定位当前 WesleyCode 应用的 skills 目录")
    parser.add_argument(
        "--resources",
        default="",
        help="逗号分隔的资源目录：scripts,references,assets",
    )
    parser.add_argument(
        "--examples",
        action="store_true",
        help="在所选资源目录中创建示例文件",
    )
    parser.add_argument(
        "--interface",
        action="append",
        default=[],
        help="以 key=value 形式覆盖 interface 字段，可重复传入",
    )
    args = parser.parse_args()

    raw_skill_name = args.skill_name
    skill_name = normalize_skill_name(raw_skill_name)
    if not skill_name:
        print("[错误] skill 名称必须至少包含一个字母或数字。")
        sys.exit(1)
    if len(skill_name) > MAX_SKILL_NAME_LENGTH:
        print(
            f"[错误] skill 名称 '{skill_name}' 过长（{len(skill_name)} 个字符），"
            f"最大允许 {MAX_SKILL_NAME_LENGTH} 个字符。"
        )
        sys.exit(1)
    if skill_name != raw_skill_name:
        print(f"提示：已将 skill 名称从 '{raw_skill_name}' 规范化为 '{skill_name}'。")

    resources = parse_resources(args.resources)
    if args.examples and not resources:
        print("[错误] 使用 --examples 时必须同时指定 --resources。")
        sys.exit(1)

    path = args.path or str(default_skills_root())

    print(f"正在初始化 skill：{skill_name}")
    print(f"   位置：{path}")
    if resources:
        print(f"   资源：{', '.join(resources)}")
        if args.examples:
            print("   示例文件：已启用")
    else:
        print("   资源：无（按需创建）")
    print()

    result = init_skill(skill_name, path, resources, args.examples, args.interface)

    if result:
        sys.exit(0)
    sys.exit(1)


if __name__ == "__main__":
    main()
