# WesleyCode

WesleyCode 是一个基于 .NET 10 的智能体宿主项目，提供控制台和 Web 两种交互入口。项目将大模型能力、命令执行、工作区文件操作和技能系统组合在一起，用于在指定工作目录内完成代码与文档类任务。

## 功能概览

- 支持控制台模式与 Web 对话模式
- 支持 `openai`、`anthropic`、`crs`、`ollama` 四类模型提供方
- 内置命令执行能力，可在工作目录中调用 PowerShell 或 Bash
- 内置工作区文件读写、搜索、列目录等工具
- 支持技能目录加载，扩展智能体能力
- 支持会话持久化，保留历史上下文
- Web 端支持查看工作区文件树、下载工作区压缩包、新建对话

## 项目结构

```text
.
├── WesleyCode.Agent/      智能体核心能力
├── WesleyCode.Console/    控制台入口
├── WesleyCode.Web/        Blazor Server Web 入口
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

- `WesleyCode.Agent/Extensions/ServiceCollectionExtensions.cs`
- `WesleyCode.Agent/Infrastructure/AgentRunner.cs`
- `WesleyCode.Agent/Services/CommandProvider.cs`
- `WesleyCode.Agent/Services/WorkspaceFilePolicyProvider.cs`

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

- 展示对话消息、工具调用与系统消息
- 展示工作区文件树
- 支持下载当前工作区压缩包
- 支持新建对话并清空工作区

入口文件：

- `WesleyCode.Web/Program.cs`
- `WesleyCode.Web/Components/Pages/Home.razor`
- `WesleyCode.Web/Services/ChatWorkspaceService.cs`

## 技术栈

- .NET 10
- C# 13
- Microsoft.Extensions.Hosting
- ASP.NET Core Blazor Server
- Microsoft.Extensions.AI
- Microsoft.Agents.AI
- OpenAI / Anthropic / Ollama 客户端
- CliWrap

## 运行前准备

### 1. 安装环境

- 安装 .NET 10 SDK
- 准备可用的大模型服务

### 2. 配置模型参数

控制台和 Web 项目都通过配置项读取模型连接参数：

- `WESLEY_PROVIDER`
- `WESLEY_MODELID`
- `WESLEY_BASEURL`
- `WESLEY_APIKEY`

支持的 `WESLEY_PROVIDER`：

- `openai`
- `anthropic`
- `crs`
- `ollama`

建议使用本地未提交的配置文件或用户机密管理敏感信息，不要把真实密钥提交到仓库。

基础配置示例：

```json
{
  "WESLEY_PROVIDER": "openai",
  "WESLEY_MODELID": "gpt-4.1",
  "WESLEY_BASEURL": "https://api.openai.com/v1",
  "WESLEY_APIKEY": "your-api-key"
}
```

## 构建项目

在仓库根目录执行：

```powershell
dotnet restore
dotnet build WesleyCode.slnx
```

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

Web 版工作区默认位于应用目录下的 `workspace` 目录，并提供：

- 新建对话
- 工作区文件树预览
- 工作区打包下载 `/workspace/archive`

## 工作机制说明

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

命令执行目录来自 `WorkingOptions.BasePath`。

### 技能加载

项目会从应用目录下的 `skills` 目录加载技能，包括：

- 系统技能
- 用户技能

这意味着可以通过增加技能文件扩展智能体行为，而不需要直接修改核心宿主逻辑。

## 开发建议

- 修改智能体系统提示时，优先查看 `WesleyCode.Agent/Extensions/ServiceCollectionExtensions.cs`
- 修改命令工具行为时，查看 `WesleyCode.Agent/Services/CommandProvider.cs`
- 修改工作区文件策略时，查看 `WesleyCode.Agent/Services/WorkspaceFilePolicyProvider.cs`
- 修改控制台交互体验时，查看 `WesleyCode.Console/Hosting/ConsoleAgentHostedService.cs`
- 修改 Web 对话界面时，查看 `WesleyCode.Web/Components/Pages/Home.razor`

## 注意事项

- 当前目标框架为 `net10.0`，构建前请确认本机 SDK 版本匹配
- Web 项目会监听工作区文件变化并实时刷新文件树
- 新建 Web 对话时会清空工作区目录内容，使用前请确认工作区内没有需要保留的文件
- 项目包含模型访问配置，建议后续统一改为安全的本地配置方案

## 后续可补充的文档

如果后续继续完善文档，建议补充：

- 本地部署说明
- 模型提供方配置样例
- 技能目录规范
- 会话存储格式说明
- Web 端交互流程截图
