# WesleyCode

WesleyCode 是一个基于 .NET 10 的智能体宿主项目，提供控制台和 Web 两种交互入口。项目将大模型能力、命令执行、工作区文件操作和技能系统组合在一起，用于在指定工作目录内完成代码与文档类任务。

## 功能概览

- 支持控制台模式与 Web 对话模式
- 支持 `openai`、`anthropic`、`crs`、`ollama` 四类模型提供方
- 内置命令执行能力，可在工作目录中调用 PowerShell 或 Bash
- 内置 HTTP/HTTPS 网络请求工具，可直接请求接口
- 内置工作区文件读写、搜索、列目录等工具
- 支持技能目录加载，扩展智能体能力
- 支持会话持久化，保留历史上下文
- Web 端支持多会话管理，每个会话拥有独立工作区和历史上下文
- 支持工作区文件预览、在线编辑、压缩包下载与检查点回滚
- 支持通过 Web 页面配置模型 Provider、Model Id 和 Base URL

## 项目结构

```text
.
├── WesleyCode.Agent/      智能体核心能力
│   ├── Extensions/        # 服务注册扩展
│   │   ├── ServiceCollectionExtensions.cs  # 主服务注册器
│   │   ├── ChatClientFactory.cs            # 模型客户端工厂（OpenAI/Anthropic/Ollama/CRS）
│   │   └── AgentModes.cs                   # Agent 模式定义（plan/execute/review）
│   ├── Infrastructure/  # 基础设施
│   │   └── AgentRunner.cs          # Agent 执行器
│   ├── Services/            # 核心服务
│   │   ├── CommandProvider.cs              # 命令执行工具
│   │   ├── NetworkRequestProvider.cs       # 网络请求工具
│   │   ├── ImageGenerationProvider.cs      # 图片生成工具
│   │   ├── WorkspaceFilePolicyProvider.cs  # 工作区文件策略
│   │   ├── SystemPromptProvider.cs         # 系统提示词
│   │   └── UserSkillsProvider.cs           # 用户技能加载
│   ├── Options/               # 配置选项
│   └── skills/              # 系统技能目录
├── WesleyCode.Console/    控制台入口
├── WesleyCode.Web/        Blazor Server Web 入口
│   ├── Components/      # Blazor 组件
│   │   ├── Pages/           # 页面组件
│   │   │   ├── Home.razor     # 主对话页面
│   │   │   ├── Error.razor    # 错误页面
│   │   │   └── NotFound.razor # 404 页面
│   │   ├── Layout/          # 布局组件
│   │   │   ├── MainLayout.razor  # 主布局（侧边栏+工作区）
│   │   │   ├── NavMenu.razor     # 导航菜单
│   │   │   └── ReconnectModal.razor # 断线重连提示
│   │   └── App.razor          # 应用根组件
│   ├── Services/        # Web 服务
│   │   ├── ChatWorkspaceService.cs  # 对话工作区服务
│   │   ├── WebOutputCapture.cs      # Web 输出捕获
│   │   └── WebOutputState.cs        # Web 输出状态管理
│   └── wwwroot/         # 静态文件
│       ├── js/              # JavaScript 文件
│       │   ├── chatComposer.js    # 聊天输入框交互
│       │   └── sidebarResize.js   # 侧边栏拖拽调整
│       ├── lib/             # 第三方库（Bootstrap）
│       ├── app.css          # 自定义样式
│       └── favicon.png      # 网站图标
├── .editorconfig
├── .gitattributes
├── .gitignore
└── WesleyCode.slnx
```

### 核心项目说明

#### `WesleyCode.Agent`

智能体核心类库，负责：

- 注册模型客户端与智能体上下文
- 提供命令执行工具
- 提供工作区文件工具
- 加载系统技能与用户技能
- 持久化和恢复会话

关键代码位置：

- `WesleyCode.Agent/Extensions/ServiceCollectionExtensions.cs` - 主服务注册器（已拆分）
- `WesleyCode.Agent/Extensions/ChatClientFactory.cs` - ChatClient 工厂方法
- `WesleyCode.Agent/Extensions/AgentModes.cs` - Agent 模式常量
- `WesleyCode.Agent/Infrastructure/AgentRunner.cs` - Agent 执行器
- `WesleyCode.Agent/Services/CommandProvider.cs` - 命令执行工具
- `WesleyCode.Agent/Services/NetworkRequestProvider.cs` - 网络请求工具
- `WesleyCode.Agent/Services/ImageGenerationProvider.cs` - 图片生成工具
- `WesleyCode.Agent/Services/WorkspaceFilePolicyProvider.cs` - 工作区文件策略

#### `WesleyCode.Console`

控制台宿主，适合直接在终端里与智能体交互。

特点：

- 启动简单，适合本地调试
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
- 展示对话消息、工具调用与系统消息
- 展示工作区文件树
- 支持下载当前工作区压缩包
- 支持新建对话并清空工作区
- 侧边栏可拖拽调整宽度
- 聊天框自动聚焦和智能滚动

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
- Microsoft.Agents.AI
- OpenAI / Anthropic / Ollama 客户端
- CliWrap
- Bootstrap 5（CSS/JS）

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

> **安全警告**：切勿将真实密钥提交到代码仓库中。建议在开发环境使用 .NET User Secrets 管理敏感信息，在生产环境中通过环境变量注入。项目中的 `.gitignore` 已配置排除 `.vs/`、`*.user` 及本地配置文件。

基础配置示例（通过 `appsettings.json` 或环境变量）：

```json
{
  "WESLEY_PROVIDER": "openai",
  "WESLEY_MODELID": "gpt-4.1",
  "WESLEY_BASEURL": "https://api.openai.com/v1",
  "WESLEY_APIKEY": "your-api-key"
}
```

图片生成配置（可选）：

```json
{
  "WESLEY_IMAGE_MODELID": "",
  "WESLEY_IMAGE_BASEURL": "",
  "WESLEY_IMAGE_APIKEY": ""
}
```

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

- **深色主题界面** - 精心设计的 CSS 变量系统，支持全局主题定制
- **多会话管理** - 新建、切换、重命名和删除历史对话
- **工作区文件树** - 实时展示工作区文件结构，侧边栏可拖拽调整宽度
- **对话消息展示** - 区分用户消息、AI 回复、系统消息和工具调用记录
- **压缩包下载** - `/workspace/archive` 端点提供当前工作区打包下载（流式输出，支持大文件）
- **智能聊天体验** - Enter 发送、Shift+Enter 换行、自动滚动到底部

工作区数据：

- Web 版工作区默认位于应用目录下的 `workspace` 目录
- 多会话数据保存在应用目录下的 `conversations` 目录
- 首次升级时，旧版 `workspace` 和对应会话会复制到"导入的对话"，原数据不会被删除

模型配置：

- 支持通过 Web 页面配置 Provider、Model Id 和 Base URL
- 设置保存至本地 `model-settings.json`，重启后生效
- API Key 不会由页面保存，仍需通过环境变量或用户机密提供
- 环境变量优先级高于页面配置

## 工作机制说明

### Agent 模式系统

项目支持三种 AI Agent 工作模式：

1. **plan（计划）** - 交互式模式，分析需求并制定详细计划
2. **execute（执行）** - 自主执行模式，自动完成任务
3. **review（审查）** - 审查验证模式，检查任务完成质量

### 会话持久化

- 控制台与 Web 宿主都依赖 `ISessionStore`
- 会话可以序列化并恢复，避免每次启动都丢失上下文

### 工作区操作

智能体优先通过工作区工具处理文件，而不是直接依赖命令行写文件。当前内置能力包括：

- 读取文件
- 保存文件
- 删除文件
- 列出文件
- 列出目录
- 正则搜索文件内容

### 命令执行

智能体可通过 `command_run` 调用系统命令：

- Windows 下默认使用 `powershell`
- 非 Windows 下默认使用 `bin/bash`
- 命令执行目录来自 `WorkingOptions.BasePath`

### 网络请求

智能体可通过 `http_request` 调用 HTTP/HTTPS 接口：

- 支持 `GET`、`POST`、`PUT`、`DELETE` 等常见方法
- 支持自定义请求头、请求体和 `Content-Type`
- 返回精简后的状态码、响应头和响应体，失败信息也放在响应体中

### 技能加载

项目会从应用目录下的 `skills` 目录加载技能，包括：

- **系统技能** - 内置技能（如 skill-creator、skill-installer）
- **用户技能** - 自定义扩展技能

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
- **新建对话按钮** - 快速创建新会话
- **下载工作区** - 一键打包下载当前工作区文件
- **文件树展示** - 实时同步工作区文件结构

### 聊天交互特性

- **智能输入框** - 支持快捷键操作（⌘/Ctrl + Enter 发送）
- **思考过程折叠** - 可展开查看 AI 的思考步骤和工具调用记录
- **自动滚动** - 新消息到达时自动滚动到底部
- **断线重连提示** - Blazor 连接断开时友好提示

## 开发建议

### Agent 核心代码

- 修改智能体系统提示时，优先查看 `WesleyCode.Agent/Extensions/AgentModes.cs`
- 添加新模型支持时，查看 `WesleyCode.Agent/Extensions/ChatClientFactory.cs`
- 修改命令工具行为时，查看 `WesleyCode.Agent/Services/CommandProvider.cs`

### Web 界面代码

- 修改工作区文件策略时，查看 `WesleyCode.Agent/Services/WorkspaceFilePolicyProvider.cs`
- 修改控制台交互体验时，查看 `WesleyCode.Console/Hosting/ConsoleAgentHostedService.cs`
- 修改 Web 对话界面时，查看：
  - `WesleyCode.Web/Components/Pages/Home.razor` - 聊天页面逻辑
  - `WesleyCode.Web/Components/Layout/MainLayout.razor` - 布局组件
  - `WesleyCode.Web/wwwroot/js/chatComposer.js` - 输入框交互逻辑

## 注意事项

- 当前目标框架为 `net10.0`，构建前请确认本机 SDK 版本匹配
- Web 项目会监听工作区文件变化并实时刷新文件树
- 新建 Web 对话时会清空工作区目录内容，使用前请确认工作区内没有需要保留的文件
- ZIP 下载端点使用流式输出，支持大工作区的打包下载
- API Key 等敏感信息应通过环境变量或 User Secrets 管理
