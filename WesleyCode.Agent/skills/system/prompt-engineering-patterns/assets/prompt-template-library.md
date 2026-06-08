# 提示词模板库

## 分类模板

### 情感分析

```text
将以下文本的情感分类为“正面”“负面”或“中性”。

文本：{text}

情感：
```

### 意图识别

```text
判断用户在以下消息中的意图。

可能意图：{intent_list}

消息：{message}

意图：
```

### 主题分类

```text
将以下文章分类到这些类别之一：{categories}

文章：
{article}

类别：
```

## 抽取模板

### 命名实体识别

```text
从文本中抽取所有命名实体并分类。

文本：{text}

实体（JSON 格式）：
{
  "persons": [],
  "organizations": [],
  "locations": [],
  "dates": []
}
```

### 结构化数据抽取

```text
从招聘信息中抽取结构化信息。

招聘信息：
{posting}

抽取信息（JSON）：
{
  "title": "",
  "company": "",
  "location": "",
  "salary_range": "",
  "requirements": [],
  "responsibilities": []
}
```

## 生成模板

### 邮件生成

```text
编写一封专业的 {email_type} 邮件。

收件人：{recipient}
上下文：{context}
需要包含的要点：
{key_points}

邮件：
主题：
正文：
```

### 代码生成

```text
为以下任务生成 {language} 代码：

任务：{task_description}

要求：
{requirements}

包含：
- 错误处理
- 输入验证
- 必要的行内注释

代码：
```

### 创意写作

```text
写一篇 {length} 字、{style} 风格、关于 {topic} 的故事。

包含这些元素：
- {element_1}
- {element_2}
- {element_3}

故事：
```

## 转换模板

### 摘要

```text
用 {num_sentences} 句话总结以下文本。

文本：
{text}

摘要：
```

### 带上下文的翻译

```text
将以下 {source_lang} 文本翻译为 {target_lang}。

上下文：{context}
语气：{tone}

文本：{text}

翻译：
```

### 格式转换

```text
将以下 {source_format} 转换为 {target_format}。

输入：
{input_data}

输出（{target_format}）：
```

## 分析模板

### 代码审查

```text
从以下方面审查代码：
1. Bug 和错误
2. 性能问题
3. 安全漏洞
4. 最佳实践违反项

代码：
{code}

审查：
```

### SWOT 分析

```text
对以下对象进行 SWOT 分析：{subject}

上下文：{context}

分析：
优势：
-

劣势：
-

机会：
-

威胁：
-
```

## 问答模板

### RAG 模板

```text
基于提供的上下文回答问题。如果上下文没有足够信息，请明确说明。

上下文：
{context}

问题：{question}

回答：
```

### 多轮问答

```text
之前的对话：
{conversation_history}

新问题：{question}

回答（自然延续对话）：
```

## 专用模板

### SQL 查询生成

```text
为以下请求生成 SQL 查询。

数据库 schema：
{schema}

请求：{request}

SQL 查询：
```

### 正则表达式创建

```text
创建一个正则表达式，用于匹配：{requirement}

应匹配的测试用例：
{positive_examples}

不应匹配的测试用例：
{negative_examples}

正则表达式：
```

### API 文档

```text
为以下函数生成 API 文档：

代码：
{function_code}

文档（遵循 {doc_format} 格式）：
```

## 使用方式

填充模板中的 `{variables}` 即可使用。
