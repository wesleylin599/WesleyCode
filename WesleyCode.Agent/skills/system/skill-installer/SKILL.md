---
name: skill-installer
description: 为 WesleyCode 应用内 `skills` 目录安装 skill，支持列出可安装技能、从精选列表安装，或从 GitHub 仓库路径安装技能（含私有仓库）。当用户询问有哪些技能可用、要求安装某个 skill，或提供仓库地址让你安装 skill 时使用。
metadata:
  short-description: 安装和管理本地 skills
---

# Skill 安装器

用于安装和管理 WesleyCode 应用内 skills。项目运行时会从 `AppContext.BaseDirectory/skills` 加载技能；这些脚本默认以自身所在位置向上查找 `skills` 根目录，适配源码目录和发布输出目录。

默认远程来源是 `https://github.com/openai/skills/tree/main/skills/.curated`，也支持用户提供其他 GitHub 仓库位置。实验性技能位于 `https://github.com/openai/skills/tree/main/skills/.experimental`，安装方式相同。

根据任务选择对应脚本：
- 当用户询问“有哪些可用 skill”或只提到这个 skill 但未说明具体操作时，列出技能。默认列出 `.curated`；如果用户问实验性技能，则传 `--path skills/.experimental`。
- 当用户直接给出 skill 名称时，从精选列表安装。
- 当用户提供 GitHub 仓库或路径时，从对应仓库安装，支持私有仓库。

优先使用配套脚本完成安装，不要手动复制文件。

## 沟通方式

列出技能时，输出格式尽量接近下面这样；如果用户问的是实验性技能，则把来源改成 `.experimental` 并在文案里明确说明：

"""
来自 {repo} 的 skills：
1. skill-1
2. skill-2（已安装）
3. ...
你想安装哪些？
"""

安装完成后，提醒用户：`重启 WesleyCode 应用后新安装的 skills 才会生效。`

## 脚本

这些脚本都需要访问网络；如果运行环境受沙箱限制，执行前要申请提升权限。

- `scripts/list-skills.py`：列出技能并标记已安装项
- `scripts/list-skills.py --format json`：以 JSON 输出技能列表
- 示例（实验性列表）：`scripts/list-skills.py --path skills/.experimental`
- `scripts/install-skill-from-github.py --repo <owner>/<repo> --path <path/to/skill> [<path/to/skill> ...]`
- `scripts/install-skill-from-github.py --url https://github.com/<owner>/<repo>/tree/<ref>/<path>`
- 示例（实验性技能）：`scripts/install-skill-from-github.py --repo openai/skills --path skills/.experimental/<skill-name>`

## 安装行为

安装脚本会：
- 如果目标 skill 目录已存在则中止，不会覆盖。
- 默认安装到当前 WesleyCode 应用的 `skills/<skill-name>`；可用 `--dest` 指定其他 skills 根目录。
- 当传多个 `--path` 时，一次安装多个 skill；每个 skill 默认使用路径最后一段作为目录名，除非显式传 `--name`。

## 注意事项

- 精选列表默认通过 GitHub API 从 `https://github.com/openai/skills/tree/main/skills/.curated` 获取；如果不可用，要把错误原因说明清楚后退出。
- `https://github.com/openai/skills/tree/main/skills/.system` 下的 skills 通常位于本项目的 `skills/system/<skill-name>`；如果用户询问这些 skill，优先直接说明通常无需重复安装。
- “已安装” 标记来自当前 WesleyCode `skills` 根目录，会同时检查 `skills/<skill-name>` 和 `skills/system/<skill-name>`。
