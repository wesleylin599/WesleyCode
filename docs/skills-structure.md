# Skills 目录结构与加载机制（调研记录）

## 结论（应遵循的结构）

- Skills 根目录位于运行时 `AppContext.BaseDirectory` 下的 `skills/`。
- 运行时会加载两类目录：
  - `skills/system/`（内置技能）
  - `skills/user/`（用户自定义技能；目前为空）
- 单个 skill 目录结构约定：
  - `skills/<scope>/<skill-slug>/SKILL.md`
  - `skills/<scope>/<skill-slug>/_meta.json`

其中 `<scope>` 为 `system` 或 `user`，`<skill-slug>` 为技能目录名/slug（例如 `find-skills`）。

## 元数据格式

- `SKILL.md`：Markdown 文档，文件头部使用 YAML Front Matter：

```yaml
---
name: find-skills
description: ...
---
```

- `_meta.json`：当前看到的字段：

```json
{
  "ownerId": "...",
  "slug": "find-skills",
  "version": "0.1.0",
  "publishedAt": 1769698710765
}
```

## 加载机制（代码位置与路径）

在 `Extensions/ServiceCollectionExtensions.cs` 中配置 `AgentSkillsProvider`：

- `systemSkills = Path.Combine(AppContext.BaseDirectory, "skills", "system")`
- `localUserSkills = Path.Combine(AppContext.BaseDirectory, "skills", "user")`
- `skillPaths: [systemSkills, localUserSkills]`

并在默认指令模板末尾明确提示：新技能应放在运行时的 `...\skills\user` 目录。

注意：仓库同时存在两套目录：

- 源码目录：`D:\source\WesleyCode\skills\system\...`
- 构建输出目录：`D:\source\WesleyCode\bin\Debug\net10.0\skills\system\...`

运行时使用的是 `AppContext.BaseDirectory`，因此实际加载的是输出目录下的 `bin\Debug\net10.0\skills\...`（或发布目录）。

## 脚本入口/运行方式约定（本仓库现状）

- 当前内置 skills（`find-skills`、`skill-creator`）均只有 `SKILL.md` + `_meta.json`，未发现配套脚本文件。
- 在代码配置中看到 `AgentSkillsProviderOptions`：
  - `ScriptApproval = false`
  - `DisableCaching = false`

由于仓库未包含带脚本的 skill 示例，本次只能确认：

- 运行时会把 skills 作为“可加载的文档指令/资源”提供给模型；
- 具体脚本入口文件命名、如何调用 bash/ps1/python/dotnet 等，需要后续在新增 skill 时按 `AgentSkillsProvider` 所支持的脚本约定验证（建议在实现新 skill 时最小化试验一个 `scripts/` 或约定文件名，并跑起来确认）。

## 依赖工具惯例（本仓库现状）

- 现有 system skills 文档中提到的外部依赖仅见 Node 生态（`npx skills ...`）。
- 未发现对 ffmpeg/yt-dlp/whisper 等工具的现有使用惯例或封装。

## 现场证据（关键文件路径）

- `D:\source\WesleyCode\Extensions\ServiceCollectionExtensions.cs`
- `D:\source\WesleyCode\skills\system\find-skills\SKILL.md`
- `D:\source\WesleyCode\skills\system\find-skills\_meta.json`
- `D:\source\WesleyCode\skills\system\skill-creator\SKILL.md`
- `D:\source\WesleyCode\skills\system\skill-creator\_meta.json`
