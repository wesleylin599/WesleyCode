# WesleyCode

WesleyCode 是一个基于 .NET 10 的智能体宿主项目，提供控制台和 Web 两种交互入口。项目将大模型能力、命令执行、工作区文件操作和技能系统组合在一起，用于在指定工作目录内完成代码与文档类任务。

## 功能概览

- 支持控制台模式与 Web 对话模式
- 支持 `openai`、`anthropic`、`crs`、`ollama` 四类模型提供方
- 内置命令执行能力，自动探测当前 Shell 环境（PowerShell / POSIX）与常用 CLI 工具版本
- 内置工作区文件读写、搜索、列目录等工具
- 支持系统技能与用户技能加载，技能脚本支持 Python / PowerShell / Node.js / C#
- 支持从工作区或应用目录读取 `AGENTS.md` 作为系统提示词
- 支持会话持久化，保留历史上下文
- Web 端支持新建对话、工作区文件树展示与压缩包下载

## 项目结构

```text
.
├── WesleyCode.Agent/      智能体核心能力
│   ├── Extensions/        # 服务注册与模型客户端
│   │   ├── ServiceCollectionExtensions.cs  # 主服务注册器（Provider / Agent / 技能）
│   │   ├── ChatClientFactory.cs            # 模型客户端工厂（OpenAI/Anthropic/Ollama/CRS）
│   │   └── AgentModes.cs                   # Agent 模式定义（plan/execute/validate）
│   ├── Infrastructure/    # 基础设施
│   │   ├── AgentRunner.cs                  # Agent 执行器
│   │   ├── SessionStore.cs                 # 会话持久化
│   │   ├── OutputAgent.cs                  # 输出代理
│   │   ├── ClaudeRelayServiceChatClient.cs # CRS 模型客户端
│   │   └── Continue*.cs / NonEmptyLoopEvaluator.cs  # 对话续接与循环评估
│   ├── Services/          # 核心服务
│   │   ├── CommandProvider.cs              # 命令执行工具（Shell 环境探测）
│   │   ├── FileSkillsProvider.cs           # skills 目录文件访问工具
│   │   ├── SystemPromptProvider.cs         # 系统提示词（AGENTS.md）
│   │   └── CliWrapRunner.cs                # 技能脚本执行器（CliWrap）
│   ├── Options/           # 配置选项
│   └── skills/system/     # 系统技能目录（skill-creator、skill-installer）
├── WesleyCode.Console/    控制台入口
│   ├── Hosting/           # 控制台交互主循环与输出捕获
│   ├── Program.cs         # 入口与配置加载
│   └── appsettings*.json  # 各环境模型参数
├── WesleyCode.Web/        Blazor Server Web 入口
│   ├── Components/        # Blazor 组件
│   │   ├── Pages/         # Home / Error / NotFound
│   │   ├── Layout/        # MainLayout / NavMenu / ReconnectModal
│   │   └── App.razor      # 应用根组件
│   ├── Services/          # ChatWorkspaceService 与 Web 输出状态
│   ├── Program.cs         # Web 入口与 /workspace/archive 端点
│   └── wwwroot/           # app.css 与 js/ 脚本
├── .github/workflows/     # CI/CD 与 GitHub Release 流水线
├── .editorconfig
├── .gitattributes
├── .gitignore
└── WesleyCode.slnx
```

### 核心项目说明

#### `WesleyCode.Agent`

智能体核心类库，负责：

- 注册模型客户端与智能体上下文
- 提供 Shell 命令执行工具（自动探测运行环境）
- 提供工作区文件工具
- 提供 skills 目录文件工具与技能脚本执行
- 从工作区或应用目录加载 `AGENTS.md` 系统提示词
- 持久化和恢复会话，支持上下文压缩

关键代码位置：

- `WesleyCode.Agent/Extensions/ServiceCollectionExtensions.cs` - 主服务注册器
- `WesleyCode.Agent/Extensions/ChatClientFactory.cs` - ChatClient 工厂方法
- `WesleyCode.Agent/Extensions/AgentModes.cs` - Agent 模式常量
- `WesleyCode.Agent/Infrastructure/AgentRunner.cs` - Agent 执行器
- `WesleyCode.Agent/Services/CommandProvider.cs` - 命令执行工具
- `WesleyCode.Agent/Services/FileSkillsProvider.cs` - skills 文件访问工具
- `WesleyCode.Agent/Services/SystemPromptProvider.cs` - 系统提示词
- `WesleyCode.Agent/Services/CliWrapRunner.cs` - 技能脚本执行器

#### `WesleyCode.Console`

控制台宿主，适合直接在终端里与智能体交互。

特点：

- 基于 PrettyPrompt 的交互输入，Spectre.Console 展示执行状态
- 支持 `/clear` 重置会话
- 支持 `/exit` 退出程序
- 执行过程中可按 `Esc` 取消当前任务

入口文件：

- `WesleyCode.Console/Program.cs`
- `WesleyCode.Console/Hosting/ConsoleAgentHostedService.cs`

#### `WesleyCode.Web`

基于 Blazor Server 的 Web 宿主，适合可视化查看对话与工作区。

特点：

- 深色主题界面设计（使用 CSS 变量）
- 展示对话消息、思考过程（工具调用）与系统消息
- 展示工作区文件树，侧边栏可拖拽调整宽度
- 支持下载当前工作区压缩包
- 支持新建对话（清空会话与工作区）
- 聊天框智能滚动与快捷键发送

入口文件：

- `WesleyCode.Web/Program.cs`
- `WesleyCode.Web/Components/Pages/Home.razor` - 主对话页面
- `WesleyCode.Web/Components/Layout/MainLayout.razor` - 主布局组件
- `WesleyCode.Web/Services/ChatWorkspaceService.cs` - 对话工作区服务

## 技术栈

- .NET 10
- C# 13
- Microsoft.Extensions.Hosting
- ASP.NET Core Blazor Server
- Microsoft.Extensions.AI
- Microsoft.Agents.AI / Microsoft.Agents.AI.Harness / Microsoft.Agents.AI.Tools.Shell
- OpenAI / Anthropic / Ollama 客户端
- CliWrap
- PrettyPrompt / Spectre.Console（控制台）

## 运行前准备

### 1. 安装环境

- 安装 .NET 10 SDK
- 准备可用的大模型服务

### 2. 配置模型参数

控制台和 Web 项目都通过配置项读取模型连接参数：

- `WESLEY_PROVIDER` - 模型提供方（openai/anthropic/crs/ollama）
- `WESLEY_MODELID` - 模型 ID
- `WESLEY_BASEURL` - API 端点地址
- `WESLEY_APIKEY` - API 密钥

支持的 `WESLEY_PROVIDER`：

- `openai` - OpenAI 兼容接口
- `anthropic` - Anthropic Claude
- `crs` - Claude Relay Service
- `ollama` - Ollama 本地模型

> **安全警告**：切勿将真实密钥提交到代码仓库中。建议在开发环境使用 .NET User Secrets 管理敏感信息，在生产环境中通过环境变量注入。项目中的 `.gitignore` 已配置排除 `.vs/`、`*.user`、`appsettings.*.json` 等本地配置文件。

基础配置示例（通过 `appsettings.json` 或环境变量）：

```json
{
  "WESLEY_PROVIDER": "openai",
  "WESLEY_MODELID": "gpt-4.1",
  "WESLEY_BASEURL": "https://api.openai.com/v1",
  "WESLEY_APIKEY": "your-api-key"
}
```

也可以为不同运行环境准备 `appsettings.<环境名>.json`（如 `appsettings.ollama.json`、`appsettings.crs.json`），程序启动时按 `DOTNET_ENVIRONMENT` / `ASPNETCORE_ENVIRONMENT` 自动加载。

## 构建项目

在仓库根目录执行：

```powershell
dotnet restore
dotnet build WesleyCode.slnx
```

## GitHub 自动发布

推送符合 `v*` 格式的标签后，GitHub Actions 会自动构建并创建 GitHub Release，上传 Console 的 Windows x64、Linux x64、macOS Intel 和 macOS Apple Silicon 单文件自包含发布包：

```bash
git tag v1.0.0
git push origin v1.0.0
```

发布包启用单文件和自包含部署，运行时不需要安装 .NET Runtime。Web 项目不参与 Release 发布。普通推送到 `master` 或提交 Pull Request 时只执行构建验证，不会创建 Release。

## 启动方式

### 启动控制台版

```powershell
dotnet run --project .\WesleyCode.Console\
```

常用控制命令：

- `/clear`：清空当前会话并重建会话
- `/exit`：退出程序
- `Esc`：取消当前正在执行的任务

默认工作目录为当前仓库根目录，会话数据保存在运行目录下的 `session` 目录。

### 启动 Web 版

```powershell
dotnet run --project .\WesleyCode.Web\
```

启动后在浏览器打开程序输出的本地地址。

Web 版功能特性：

- **深色主题界面** - 基于 CSS 变量的现代深色设计
- **对话消息展示** - 区分用户消息、AI 回复、系统消息和思考过程（工具调用）记录
- **工作区文件树** - 实时展示工作区文件结构，侧边栏可拖拽调整宽度
- **压缩包下载** - `/workspace/archive` 端点提供当前工作区打包下载
- **新建对话** - 一键清空会话与工作区并开始新任务
- **智能聊天体验** - Ctrl/⌘ + Enter 发送、Shift + Enter 换行、自动滚动、执行中可中断

工作区数据：

- Web 版工作区默认位于应用目录下的 `workspace` 目录
- 会话数据保存在应用目录下的 `session` 目录
- 新建对话会清空当前工作区内容，请注意提前备份需要保留的文件

## 工作机制说明

### Agent 模式系统

项目支持三种 AI Agent 工作模式：

1. **plan（计划）** - 交互式模式，分析需求、制定计划，并在继续前向用户澄清
2. **execute（执行）** - 自主执行模式，自动完成任务
3. **validate（验证）** - 审查验证模式，对照原始需求检查执行结果；验证失败会切换回 execute 修正

### 会话持久化

- 控制台与 Web 宿主都依赖 `ISessionStore`
- 会话可以序列化并恢复，避免每次启动都丢失上下文

### 工作区操作

智能体优先通过文件工具处理工作区文件，而不是直接依赖命令行写文件。当前内置能力包括：

- 列出目录与子项
- 读取文件
- 保存文件
- 删除文件
- 按正则表达式搜索文件内容

### 命令执行

智能体可通过 Shell 工具调用系统命令，`CommandProvider` 在每次提供上下文时会自动探测运行环境：

- 识别当前 Shell 类型（Windows 为 PowerShell，其他平台为 POSIX shell）及版本
- 获取当前工作目录
- 探测 `git`、`dotnet`、`node`、`python`、`docker`、`curl` 是否可用及其版本
- 根据探测结果生成 Shell 使用指引（如 PowerShell 使用 `$env:NAME` 设置环境变量、使用 `Out-Null` 抑制输出），避免误用其他 Shell 语法

底层使用 `LocalShellExecutor`，命令执行目录限定在 `WorkingOptions.BasePath`，单条命令超时 5 分钟。

### 系统提示词

`SystemPromptProvider` 会拼接操作系统、工作目录信息，并依次加载工作区根目录与应用目录下的 `AGENTS.md`（旧版 `SYSTEM.md` 已废弃），用于在工作区中放置针对性的项目规则。

### 技能加载

项目会从应用目录下的 `skills` 目录加载技能，包括：

- **系统技能** - 内置技能（如 skill-creator、skill-installer）
- **用户技能** - 自定义扩展技能

`FileSkillsProvider` 为智能体提供 skills 目录的列表、读取、保存、删除与搜索工具；技能脚本由 `CliWrapRunner` 执行，支持 `.py`、`.ps1`、`.js/.mjs/.cjs`、`.cs/.csx` 脚本。

这意味着可以通过增加技能文件扩展智能体行为，而不需要直接修改核心宿主逻辑。

## Web 界面说明

### 深色主题设计

Web 端采用现代深色主题设计，通过 CSS 变量实现全局风格控制：

```css
:root {
    --canvas: #080b10;        /* 画布底色 */
    --surface: rgba(24, 29, 39, 0.72);  /* 表面层 */
    --text: #f4f6f8;           /* 主要文字 */
    --accent: #62d6a7;         /* 强调色（绿色） */
    --blue: #72a7ff;           /* 蓝色 */
    --amber: #f3bd63;          /* 琥珀色 */
    --red: #ff8e88;            /* 红色警告 */
}
```

### 侧边栏功能

- **品牌标识** - WesleyCode 智能开发工作台
- **运行状态指示器** - 显示 AI 当前是否正在处理任务
- **新建对话按钮** - 清空会话与工作区并开始新对话
- **下载工作区** - 一键打包下载当前工作区文件
- **文件树展示** - 实时同步工作区文件结构，可拖拽调整侧边栏宽度

### 聊天交互特性

- **智能输入框** - Ctrl/⌘ + Enter 发送，Shift + Enter 换行
- **思考过程折叠** - 可展开查看 AI 的思考步骤和工具调用记录
- **执行中断** - 生成过程中可一键中断当前任务
- **自动滚动** - 新消息到达时自动滚动到底部
- **断线重连提示** - Blazor 连接断开时友好提示

## 开发建议

### Agent 核心代码

- 修改智能体工作模式时，查看 `WesleyCode.Agent/Extensions/AgentModes.cs`
- 添加新模型支持时，查看 `WesleyCode.Agent/Extensions/ChatClientFactory.cs`
- 修改命令工具行为时，查看 `WesleyCode.Agent/Services/CommandProvider.cs` 与 `WesleyCode.Agent/Extensions/ServiceCollectionExtensions.cs`
- 修改系统提示词加载逻辑时，查看 `WesleyCode.Agent/Services/SystemPromptProvider.cs`
- 修改技能加载与脚本执行时，查看 `WesleyCode.Agent/Services/FileSkillsProvider.cs` 与 `WesleyCode.Agent/Services/CliWrapRunner.cs`

### Web 界面代码

- 修改控制台交互体验时，查看 `WesleyCode.Console/Hosting/ConsoleAgentHostedService.cs`
- 修改 Web 对话界面时，查看：
  - `WesleyCode.Web/Components/Pages/Home.razor` - 聊天页面逻辑
  - `WesleyCode.Web/Components/Layout/MainLayout.razor` - 布局组件
  - `WesleyCode.Web/wwwroot/js/chatComposer.js` - 输入框交互逻辑
  - `WesleyCode.Web/wwwroot/js/sidebarResize.js` - 侧边栏拖拽逻辑

## 注意事项

- 当前目标框架为 `net10.0`，构建前请确认本机 SDK 版本匹配
- Web 项目会监听工作区文件变化并实时刷新文件树
- 新建 Web 对话时会清空工作区目录内容，使用前请确认工作区内没有需要保留的文件
- `/workspace/archive` 在内存中打包工作区后返回，工作区文件过大时注意内存占用
- API Key 等敏感信息应通过环境变量、User Secrets 或本地 `appsettings.*.json`（已 gitignore）管理
