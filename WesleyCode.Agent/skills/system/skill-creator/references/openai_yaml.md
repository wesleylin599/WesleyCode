# openai.yaml 参考

`agents/openai.yaml` 用于定义 skill 的界面展示元数据，例如名称、简述、图标和默认提示词。

## 基本结构

```yaml
interface:
  display_name: "技能名称"
  short_description: "面向用户的简短说明"
  icon_small: "./assets/icon-small.svg"
  icon_large: "./assets/icon-large.png"
  default_prompt: "默认插入的提示词"
```

## 字段说明

- `interface.display_name`
  - 展示名称，应面向人类用户，简洁清晰。
  - 建议直接体现 skill 的用途，而不是目录名。

- `interface.short_description`
  - 列表页和卡片中显示的简短说明。
  - 建议简洁、明确，直接描述 skill 能帮助完成什么。
  - 当前项目脚本默认要求长度在 8 到 64 个字符之间。

- `interface.icon_small`
  - 小图标路径，通常用于列表或标签。

- `interface.icon_large`
  - 大图标路径，通常用于详情或较大卡片。

- `interface.brand_color`
  - 可选，指定品牌色。

- `interface.default_prompt`
  - 可选，调用该 skill 时默认注入的提示词片段。

## 生成建议

- 优先根据 `SKILL.md` 的真实内容生成这些字段。
- 如果 skill 内容变了，记得同步刷新 `agents/openai.yaml`。
- 没有明确需求时，不必强行填写可选字段。
