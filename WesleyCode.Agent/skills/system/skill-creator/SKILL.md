---
name: skill-creator
description: 用于创建或更新 WesleyCode 应用内可加载的 skills，适用于需要把特定领域知识、工作流、脚本或工具整合成可复用 skill 的场景。当用户要求新建 skill、改造现有 skill、补充 skill 资源或优化 skill 触发说明时使用。
metadata:
  short-description: 创建或更新本地 skills
---

# Skill 创建器

用于创建和维护高质量的 skills。

## Skills 是什么

Skill 是自包含的目录，通过提供专门知识、工作流和工具来扩展 Codex 能力。你可以把它理解为某个领域的“上手指南”，用于把通用智能体变成能稳定处理特定任务的专用智能体。

### Skill 提供什么

1. 专项工作流：面向特定领域的多步骤操作流程
2. 工具集成：如何处理特定文件格式、脚本或 API 的说明
3. 领域知识：公司规则、数据结构、业务背景等上下文
4. 打包资源：用于复杂或重复任务的脚本、参考资料和素材文件

## 核心原则

### 保持精炼

上下文窗口是公共资源。Skill 会和系统提示词、对话历史、其他 skills 元数据以及用户请求共享同一份上下文预算。

默认假设：Codex 已经足够聪明。只补充它确实不知道、但完成任务又必需的信息。持续追问自己两件事：
- 这段解释是不是模型本来就知道？
- 这段内容值不值得消耗上下文？

优先给简洁示例，而不是冗长说明。

### 控制自由度

根据任务的脆弱性和变化度，决定约束强弱：
- 高自由度：适合多种解法都可行、需要按上下文判断的任务，用自然语言规则说明即可。
- 中自由度：适合常见流程，给步骤、判断点和关键注意事项。
- 低自由度：适合容易出错或要求严格一致的任务，用固定步骤、模板、脚本或检查清单。

### 让 Skill 可复用

Skill 的目标不是记录一次性答案，而是让另一个 Codex 在相似任务中反复稳定使用。优先沉淀：
- 可重复执行的脚本
- 只在需要时加载的参考资料
- 可以直接复用的模板或素材

## Skill 结构

每个 skill 至少包含一个 `SKILL.md`，也可以按需附带资源：

```text
skill-name/
├── SKILL.md
├── scripts/
├── references/
├── assets/
└── agents/
    └── openai.yaml
```

### `SKILL.md`

每个 `SKILL.md` 包含两部分：
- Frontmatter（YAML）：至少包含 `name` 和 `description`。这是 skill 触发判断最关键的元数据。
- Body（Markdown）：在 skill 触发后才会加载，写使用说明、流程和资源导航。

### `agents/openai.yaml`

用于技能列表和 UI 展示的元数据。创建或更新 skill 时：
- 先阅读 skill 内容，再生成人类可读的 `display_name`、`short_description`、`default_prompt`
- 通过 `scripts/generate_openai_yaml.py` 或 `scripts/init_skill.py --interface key=value` 生成
- 如果 skill 内容改了，记得同步检查 `agents/openai.yaml` 是否过期
- 图标、品牌色这类可选字段，只有用户明确需要时再加

### 可选资源目录

#### `scripts/`

放可执行脚本，用于需要确定性、重复率高的操作。

适合场景：
- 反复会写到同一段自动化代码
- 人工执行容易出错
- 想把复杂步骤变成固定工具

#### `references/`

放参考资料，只在需要时读取。

适合场景：
- API 文档
- 数据库结构
- 领域规则
- 详细流程说明

#### `assets/`

放输出时要用到的文件，而不是给模型阅读的说明文本。

适合场景：
- 模板文件
- 图片、图标、字体
- 样板工程
- 示例数据

## 渐进加载原则

Skill 应尽量分层加载，避免一开始塞太多内容：
1. 元数据：`name` + `description`，始终参与触发判断
2. `SKILL.md` 正文：skill 触发后加载
3. 资源目录：按需读取或执行

做法建议：
- `SKILL.md` 正文只保留核心流程和导航
- 详细资料拆到 `references/`
- 可执行逻辑拆到 `scripts/`
- 大文件要在 `SKILL.md` 里明确说明什么时候读、读哪个文件
- 避免多层级嵌套引用，尽量从 `SKILL.md` 直接链接到所需文件

## Skill 创建流程

### 1. 先理解 skill 的真实使用方式

只有当使用场景已经非常明确时，才可以跳过这一步。

先收集具体例子，弄清楚：
- 这个 skill 要解决什么问题
- 用户会怎么触发它
- 哪些文件、脚本或知识是重复需要的
- skill 最终应该放到哪里

如果用户没有指定位置，默认放到当前 WesleyCode 应用的 `skills` 根目录。脚本会从自身路径向上查找名为 `skills` 的目录；也可以用 `WESLEY_SKILLS_ROOT` 或 `--path` 显式指定。

### 2. 规划可复用资源

把用户示例拆成可复用能力，判断哪些内容应沉淀为：
- `scripts/`：固定脚本
- `references/`：说明文档、规范、结构信息
- `assets/`：模板、图标、样板工程

### 3. 初始化 skill

如果是新建 skill，优先运行 `init_skill.py` 生成标准目录，而不是手工搭结构。

```bash
scripts/init_skill.py <skill-name> --path <output-directory> [--resources scripts,references,assets] [--examples]
```

示例：

```bash
scripts/init_skill.py my-skill
scripts/init_skill.py my-skill --resources scripts,references
scripts/init_skill.py my-skill --path D:/source/WesleyCode/WesleyCode.Agent/skills --resources scripts --examples
```

脚本会：
- 创建 skill 目录
- 生成带 frontmatter 的 `SKILL.md` 模板
- 生成 `agents/openai.yaml`
- 按需创建资源目录
- 可选生成示例文件

### 4. 编辑 skill 内容

重点补全：
- `SKILL.md` 的触发描述和流程说明
- `scripts/` 中实际要执行的脚本
- `references/` 中必要的参考资料
- `assets/` 中实际要复用的文件

如果用了示例文件，确认是否要替换或删除占位内容。

### 5. 校验 skill

完成后运行：

```bash
scripts/quick_validate.py <path/to/skill-folder>
```

这个脚本会检查：
- YAML frontmatter 是否有效
- 是否包含必填字段
- skill 命名是否符合规则

### 6. 迭代优化

在真实任务里使用 skill 后，继续根据效果优化：
- 哪些说明仍然不够清晰
- 哪些流程可以进一步脚本化
- 哪些参考资料应该拆分或补充
- 是否需要补充更准确的触发描述

## 编写要求

### Frontmatter

Frontmatter 至少包含：
- `name`
- `description`

其中 `description` 是最关键的触发说明，必须写清：
- skill 能做什么
- 在什么场景下应该触发
- 典型文件类型、任务类型或上下文线索

不要把“何时使用这个 skill”只写在正文里，因为正文只会在触发后加载。

### 正文

正文要面向另一个 Codex 编写，强调：
- 怎么做
- 用哪些资源
- 遇到不同场景怎么选择路径
- 哪些操作必须验证

## 前向测试

当 skill 比较复杂时，应该用真实任务进行前向测试。测试时：
- 用接近真实用户请求的提示
- 直接提供 skill 和原始材料
- 不要把你预期的答案泄漏给测试智能体
- 每轮迭代后重新验证效果

如果只有在泄漏上下文的情况下才“测试成功”，说明 skill 本身还不够稳，需要继续收紧说明或补足资源。
