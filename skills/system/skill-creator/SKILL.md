---
name: skill-creator
description: 创建高质量技能的指南。当用户想创建新技能（或更新现有技能），以通过专门的知识、工作流或工具集成来扩展 Claude 的能力时，应使用此技能。
license: Complete terms in LICENSE.txt
---

# 技能创建指南

本技能提供创建高质量技能的指导。

## 关于 Skills

Skills 是模块化、自包含的包，通过提供专门的知识、工作流和工具来扩展 Claude 的能力。可以把它们看作针对特定领域或任务的“上手指南”——它们把 Claude 从通用智能体转变为具备流程化知识的专业智能体，而这些程序性知识并非任何模型都能完整内化。
specialized knowledge, workflows, and tools. Think of them as "onboarding guides" for specific
domains or tasks—they transform Claude from a general-purpose agent into a specialized agent
equipped with procedural knowledge that no model can fully possess.

### Skills 提供什么

1. 专门的工作流 - 面向特定领域的多步骤流程
2. 工具集成 - 与特定文件格式或 API 协作的说明
3. 领域知识 - 公司特定知识、Schema、业务逻辑
4. 打包资源 - 用于复杂或重复任务的脚本、参考资料与资产

## 核心原则

### 简洁至上

上下文窗口是公共资源。Skills 会与 Claude 需要的其它内容共享上下文窗口：系统提示词、对话历史、其他 Skills 的元数据，以及实际用户请求。

**默认假设：Claude 已经很聪明。** 只添加 Claude 本来不会自动具备的上下文。对每段信息都要自问："Claude 真的需要这段解释吗？" 以及 "这段文字是否值得它的 token 成本？"

优先用简短示例替代冗长解释。

### 设定合适的自由度

根据任务的脆弱性和可变性，匹配合适的具体程度：

**高自由度（文本说明）**：多种做法都可行、决策依赖上下文或需要启发式判断时使用。

**中自由度（伪代码或带参数脚本）**：有推荐范式、允许一定变化，或配置会影响行为时使用。

**低自由度（具体脚本、少量参数）**：操作脆弱易错、需要一致性，或必须遵循特定顺序时使用。

把 Claude 想象为在探索一条路径：狭窄的桥（低自由度）需要明确护栏；开阔的原野（高自由度）允许多条路线。

### Skill 的结构

每个 skill 都由必需的 SKILL.md 文件和可选的打包资源组成：

```
skill-name/
├── SKILL.md (required)
│   ├── YAML frontmatter metadata (required)
│   │   ├── name: (required)
│   │   └── description: (required)
│   └── Markdown instructions (required)
└── Bundled Resources (optional)
    ├── scripts/          - Executable code (Python/Bash/etc.)
    ├── references/       - Documentation intended to be loaded into context as needed
    └── assets/           - Files used in output (templates, icons, fonts, etc.)
```

#### SKILL.md（必需）

每个 SKILL.md 由以下部分构成：

- **Frontmatter**（YAML）：包含 `name` 与 `description` 字段。这两项是 Claude 用来判断何时使用该技能的唯一信息，因此必须清晰、全面地描述技能是什么，以及应在什么情况下使用。
- **正文**（Markdown）：使用该技能的说明与指南。仅在技能触发之后才会加载（如果需要）。

#### 打包资源（可选）

##### 脚本（`scripts/`）

用于需要确定性可靠性或需要反复重写的任务的可执行代码（Python/Bash 等）。

- **何时包含**：当相同代码会被反复重写，或需要确定性可靠性时
- **示例**：用于 PDF 旋转的 `scripts/rotate_pdf.py`
- **收益**：更省 token、结果更确定、可在不加载到上下文的情况下执行
- **注意**：脚本仍可能需要被 Claude 读取，以便打补丁或做环境相关调整

##### 参考资料（`references/`）

用于在需要时加载到上下文、以指导 Claude 思考与流程的文档与参考材料。

- **何时包含**：当存在 Claude 在工作时应参考的文档
- **示例**：财务 schema 的 `references/finance.md`、公司 NDA 模板的 `references/mnda.md`、公司政策的 `references/policies.md`、API 规范的 `references/api_docs.md`
- **用例**：数据库 schema、API 文档、领域知识、公司政策、详细工作流指南
- **收益**：保持 SKILL.md 精简，仅在需要时加载
- **最佳实践**：若文件很大（>10k 词），在 SKILL.md 中提供 grep 搜索模式
- **避免重复**：信息应存在于 SKILL.md 或 references 文件之一，而不是两者都写。除非确实是技能核心，否则优先把详细信息放在 references 中；SKILL.md 只保留关键流程说明与工作流引导。

##### 资源（`assets/`）

不用于加载到上下文，而是在 Claude 产出结果时直接使用的文件。

- **何时包含**：当技能需要在最终输出中使用这些文件时
- **示例**：品牌资源 `assets/logo.png`、PowerPoint 模板 `assets/slides.pptx`、HTML/React 模板 `assets/frontend-template/`、字体 `assets/font.ttf`
- **用例**：模板、图片、图标、样板代码、字体、会被复制或修改的示例文档
- **收益**：将输出资源与文档分离，使 Claude 能在不加载上下文的情况下使用文件

#### 不要在 Skill 中包含什么

Skill 应只包含直接支撑其功能的必要文件。不要创建额外的文档或辅助文件，包括但不限于：

- README.md
- INSTALLATION_GUIDE.md
- QUICK_REFERENCE.md
- CHANGELOG.md
- etc.

Skill 应只包含 AI 智能体完成工作所需的信息。不应包含关于制作过程、搭建与测试流程、面向用户的文档等辅助上下文。增加额外文档只会带来混乱。

### 渐进式披露（Progressive Disclosure）设计原则

Skills 使用三层加载机制来高效管理上下文：

1. **元数据（name + description）** - 始终在上下文中（约 100 词）
2. **SKILL.md 正文** - 技能触发时（<5k 词）
3. **打包资源** - Claude 需要时再用（理论上不受限，因为脚本可执行而无需读入上下文窗口）

#### 渐进式披露模式

将 SKILL.md 正文控制在必要内容内，并尽量少于 500 行以减少上下文膨胀。接近上限时拆分到独立文件；拆分后必须在 SKILL.md 中明确引用，并说明何时需要读取，以确保使用该 skill 的人知道它们的存在与使用时机。

**关键原则：** 当 skill 支持多种变体、框架或选项时，在 SKILL.md 中只保留核心工作流与选择指引，把特定变体的细节（模式、示例、配置）移到参考文件中。

**Pattern 1: High-level guide with references**

```markdown
# PDF Processing

## Quick start

Extract text with pdfplumber:
[code example]

## Advanced features

- **Form filling**: See [FORMS.md](FORMS.md) for complete guide
- **API reference**: See [REFERENCE.md](REFERENCE.md) for all methods
- **Examples**: See [EXAMPLES.md](EXAMPLES.md) for common patterns
```

Claude 只在需要时加载 FORMS.md、REFERENCE.md 或 EXAMPLES.md。

**模式 2：领域化组织**

对于包含多个领域的 Skills，按领域组织内容以避免加载无关上下文：

```
bigquery-skill/
├── SKILL.md (overview and navigation)
└── reference/
    ├── finance.md (revenue, billing metrics)
    ├── sales.md (opportunities, pipeline)
    ├── product.md (API usage, features)
    └── marketing.md (campaigns, attribution)
```

当用户询问销售指标时，Claude 只读取 sales.md。

类似地，对于支持多个框架或变体的技能，可按变体组织：

```
cloud-deploy/
├── SKILL.md (workflow + provider selection)
└── references/
    ├── aws.md (AWS deployment patterns)
    ├── gcp.md (GCP deployment patterns)
    └── azure.md (Azure deployment patterns)
```

当用户选择 AWS 时，Claude 只读取 aws.md。

**模式 3：按条件加载细节**

展示基础内容，并链接到高级内容：

```markdown
# DOCX Processing

## Creating documents

Use docx-js for new documents. See [DOCX-JS.md](DOCX-JS.md).

## Editing documents

For simple edits, modify the XML directly.

**For tracked changes**: See [REDLINING.md](REDLINING.md)
**For OOXML details**: See [OOXML.md](OOXML.md)
```

Claude 只在用户需要这些功能时读取 REDLINING.md 或 OOXML.md。

**重要指南：**

- **避免过深的引用链** - references 不要层层嵌套；所有参考文件应从 SKILL.md 直接链接到
- **为长参考文件做结构化** - 超过 100 行的文件在顶部加入目录，便于预览范围

## Skill 创建流程

Skill 创建通常包含以下步骤：

1. Understand the skill with concrete examples
2. Plan reusable skill contents (scripts, references, assets)
3. Initialize the skill (run init_skill.py)
4. Edit the skill (implement resources and write SKILL.md)
5. Package the skill (run package_skill.py)
6. Iterate based on real usage

按顺序执行这些步骤，只有在明确不适用时才跳过。

### 第 1 步：用具体示例理解 Skill

只有当技能的使用模式已经非常清晰时才可跳过该步骤。即便是在改进现有技能时，它也很有价值。

要创建有效的 skill，需要清楚了解它会如何被使用的具体示例。这些示例可以来自用户提供的真实案例，也可以来自你生成并通过用户反馈验证的示例。

例如，构建一个 image-editor skill 时，可参考这些问题：

- "What functionality should the image-editor skill support? Editing, rotating, anything else?"
- "Can you give some examples of how this skill would be used?"
- "I can imagine users asking for things like 'Remove the red-eye from this image' or 'Rotate this image'. Are there other ways you imagine this skill being used?"
- "What would a user say that should trigger this skill?"

为避免压垮用户，不要在一条消息里问太多问题。先问最重要的问题，再根据需要追问。

当你对 skill 要支持的功能范围有清晰把握时，结束该步骤。

### 第 2 步：规划可复用的技能内容

把具体示例转化为有效的 skill 时，逐个分析每个示例：

1. 设想从零开始如何执行该示例
2. 识别哪些脚本、参考资料和资产能在反复执行这些工作流时提供帮助

示例：构建用于处理 "Help me rotate this PDF" 的 `pdf-editor` skill，分析后会发现：

1. 旋转 PDF 需要每次都重写类似代码
2. 可以把 `scripts/rotate_pdf.py` 脚本作为技能资源保存

示例：设计用于 "Build me a todo app" 或 "Build me a dashboard to track my steps" 的 `frontend-webapp-builder` skill，分析后会发现：

1. 开发前端应用往往需要重复编写相同的 HTML/React 样板
2. 可在 `assets/hello-world/` 中存放样板项目文件

示例：构建用于处理 "How many users have logged in today?" 的 `big-query` skill，分析后会发现：

1. 查询 BigQuery 往往需要每次重新摸清表结构与关系
2. 可用 `references/schema.md` 记录表结构作为参考

为了确定技能内容，对每个示例进行分析并列出需要包含的可复用资源：脚本、参考资料和资产。

### 第 3 步：初始化 Skill

此时可以开始真正创建 skill。

只有在技能已存在且你只需要迭代或打包时才跳过该步骤；否则继续下一步。

从零创建新 skill 时，始终运行 `init_skill.py` 脚本。它会生成包含所有必需内容的技能目录模板，能显著提高创建效率与可靠性。

Usage:

```bash
scripts/init_skill.py <skill-name> --path <output-directory>
```

该脚本会：

- 在指定路径创建 skill 目录
- 生成带有正确 frontmatter 与 TODO 占位符的 SKILL.md 模板
- 创建示例资源目录：`scripts/`、`references/`、`assets/`
- 在每个目录中添加可自定义或删除的示例文件

初始化后，按需自定义或删除生成的 SKILL.md 和示例文件。

### 第 4 步：编辑 Skill

编辑（新生成或已有）的 skill 时，要记住你是在为另一个 Claude 实例编写它。应加入对 Claude 有帮助且不那么显而易见的信息。思考哪些流程知识、领域细节或可复用资产能让另一个 Claude 更高效地执行这些任务。

#### 学习成熟的设计模式

根据 skill 的需求参考这些指南：

- **多步骤流程**：参见 references/workflows.md，了解顺序工作流与条件逻辑
- **特定输出格式或质量标准**：参见 references/output-patterns.md，了解模板与示例模式

这些文件包含有效的 skill 设计最佳实践。

#### 从可复用内容入手

开始实现时，优先落地前面识别出的可复用资源：`scripts/`、`references/`、`assets/`。注意：此步骤可能需要用户输入。例如实现 `brand-guidelines` skill 时，用户可能需要提供要存入 `assets/` 的品牌资源或模板，或要存入 `references/` 的文档。

新增脚本必须通过实际运行来测试，确保没有 bug 且输出符合预期。如果脚本很多且相似，只需抽样测试代表性脚本即可，在完成速度与信心之间取得平衡。

任何不需要的示例文件或目录都应删除。初始化脚本会在 `scripts/`、`references/`、`assets/` 中创建示例文件以演示结构，但大多数技能不需要全部保留。

#### 更新 SKILL.md

**写作指南：** 始终使用祈使/不定式表达。

##### Frontmatter

用 `name` 与 `description` 编写 YAML frontmatter：

- `name`：技能名称
- `description`：这是技能最主要的触发机制，帮助 Claude 理解何时使用该技能。
  - 同时包含技能做什么，以及明确的触发语境/上下文
  - 所有“何时使用”的信息都写在这里 - 不要放在正文里。正文只有触发后才加载，因此正文里的 "When to Use This Skill" 章节对 Claude 帮助不大。
  - `docx` 技能的 description 示例："全面的文档创建、编辑与分析，支持修订模式（tracked changes）、批注、格式保真与文本提取。用于 Claude 需要处理专业文档（.docx）时：(1) 创建新文档，(2) 修改或编辑内容，(3) 处理修订模式，(4) 添加批注，或任何其他文档任务"

不要在 YAML frontmatter 中包含其他字段。

##### 正文

编写使用该 skill 及其打包资源的说明。

### 第 5 步：打包 Skill

当 skill 开发完成后，需要将其打包为可分发的 .skill 文件并分享给用户。打包流程会先自动校验技能是否满足要求：

```bash
scripts/package_skill.py <path/to/skill-folder>
```

可选：指定输出目录：

```bash
scripts/package_skill.py <path/to/skill-folder> ./dist
```

打包脚本将会：

1. **自动校验** skill，检查：

   - YAML frontmatter format and required fields
   - Skill naming conventions and directory structure
   - Description completeness and quality
   - File organization and resource references

2. **打包**：校验通过后生成以技能命名的 .skill 文件（例如：`my-skill.skill`），其中包含所有文件并保持正确的目录结构。.skill 文件本质上是带 .skill 扩展名的 zip 包。

如果校验失败，脚本会报告错误并退出，不会创建包。修复后重新运行打包命令。

### 第 6 步：迭代

在测试并使用 skill 后，用户可能会提出改进请求。通常这会发生在刚使用完技能之后，因为当时对技能表现的上下文最完整。

**迭代工作流：**

1. 在真实任务中使用该 skill
2. 观察卡点或低效之处
3. 确认应如何更新 SKILL.md 或打包资源
4. 实施改动并再次测试
