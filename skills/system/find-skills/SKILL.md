---
name: find-skills
description: 当用户提出诸如 "how do I do X"、"find a skill for X"、"is there a skill that can..." 之类的问题，或表达希望扩展能力时，帮助用户发现并安装智能体技能。当用户在寻找一种可能以可安装技能形式存在的功能时，应使用此技能。
---

# 查找技能

本技能帮助你在开放的智能体技能生态中发现并安装技能。

## 何时使用本技能

当用户出现以下情况时使用本技能：

- 询问 "how do I do X"，其中 X 可能是常见任务且已有对应技能
- 说 "find a skill for X" 或 "is there a skill for X"
- 询问 "can you do X"，其中 X 属于专业能力
- 表达想要扩展智能体能力
- 想搜索工具、模板或工作流
- 提到希望在某个特定领域获得帮助（设计、测试、部署等）

## 什么是 Skills CLI？

Skills CLI（`npx skills`）是开放智能体技能生态的包管理器。Skills 是模块化包，通过提供专门的知识、工作流和工具来扩展智能体能力。

**常用命令：**

- `npx skills find [query]` - 交互式或按关键字搜索技能
- `npx skills add <package>` - 从 GitHub 或其他来源安装技能
- `npx skills check` - 检查技能更新
- `npx skills update` - 更新所有已安装技能

**浏览技能：** https://skills.sh/

## 如何帮助用户查找技能

### 第 1 步：理解需求

当用户请求帮助时，识别：

1. 领域（例如：React、测试、设计、部署）
2. 具体任务（例如：写测试、做动画、审 PR）
3. 这是否足够常见，以至于很可能存在对应技能

### 第 2 步：搜索技能

使用合适的查询运行 find 命令：

```bash
npx skills find [query]
```

例如：

- 用户问 "how do I make my React app faster?" → `npx skills find react performance`
- 用户问 "can you help me with PR reviews?" → `npx skills find pr review`
- 用户说 "I need to create a changelog" → `npx skills find changelog`

该命令会返回类似结果：

```
Install with npx skills add <owner/repo@skill>

vercel-labs/agent-skills@vercel-react-best-practices
└ https://skills.sh/vercel-labs/agent-skills/vercel-react-best-practices
```

### 第 3 步：向用户展示选项

当你找到相关技能时，向用户提供：

1. 技能名称及其用途
2. 可执行的安装命令
3. 在 skills.sh 了解更多的链接

示例回复：

```
我找到了一个可能有帮助的技能！"vercel-react-best-practices" 技能提供来自 Vercel Engineering 的 React 和 Next.js 性能优化指南。
React and Next.js performance optimization guidelines from Vercel Engineering.

安装方式：
npx skills add vercel-labs/agent-skills@vercel-react-best-practices

了解更多： https://skills.sh/vercel-labs/agent-skills/vercel-react-best-practices
```

### 第 4 步：主动提供安装

如果用户希望继续，你可以替他们安装技能：

```bash
npx skills add <owner/repo@skill> -g -y
```

`-g` 表示全局（用户级）安装，`-y` 跳过确认提示。

## 常见技能分类

搜索时可参考这些常见类别：

| Category        | Example Queries                          |
| --------------- | ---------------------------------------- |
| Web Development | react, nextjs, typescript, css, tailwind |
| Testing         | testing, jest, playwright, e2e           |
| DevOps          | deploy, docker, kubernetes, ci-cd        |
| Documentation   | docs, readme, changelog, api-docs        |
| Code Quality    | review, lint, refactor, best-practices   |
| Design          | ui, ux, design-system, accessibility     |
| Productivity    | workflow, automation, git                |

## 有效搜索的小技巧

1. **使用更具体的关键词**："react testing" 比只搜 "testing" 更好
2. **尝试同义或近义词**：如果 "deploy" 没结果，试试 "deployment" 或 "ci-cd"
3. **关注常见来源**：很多技能来自 `vercel-labs/agent-skills` 或 `ComposioHQ/awesome-claude-skills`

## 当找不到技能时

如果没有相关技能：

1. 说明未找到现成技能
2. 提供直接用通用能力协助完成任务
3. 建议用户用 `npx skills init` 创建自己的技能

示例：

```
我搜索了与 "xyz" 相关的技能，但没有找到匹配项。
我仍然可以直接帮你完成这个任务！你希望我继续吗？

如果这是你经常需要的能力，你也可以创建自己的技能：
npx skills init my-xyz-skill
```
