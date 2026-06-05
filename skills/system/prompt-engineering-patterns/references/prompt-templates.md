# 提示词模板系统

## 模板架构

### 基础模板结构

```python
class PromptTemplate:
    def __init__(self, template_string, variables=None):
        self.template = template_string
        self.variables = variables or []

    def render(self, **kwargs):
        missing = set(self.variables) - set(kwargs.keys())
        if missing:
            raise ValueError(f"Missing required variables: {missing}")

        return self.template.format(**kwargs)

# 用法
template = PromptTemplate(
    template_string="Translate {text} from {source_lang} to {target_lang}",
    variables=['text', 'source_lang', 'target_lang']
)

prompt = template.render(
    text="Hello world",
    source_lang="English",
    target_lang="Spanish"
)
```

### 条件模板

```python
class ConditionalTemplate(PromptTemplate):
    def render(self, **kwargs):
        # 处理条件块
        result = self.template

        # 处理 if 块：{{#if variable}}content{{/if}}
        import re
        if_pattern = r'\{\{#if (\w+)\}\}(.*?)\{\{/if\}\}'

        def replace_if(match):
            var_name = match.group(1)
            content = match.group(2)
            return content if kwargs.get(var_name) else ''

        result = re.sub(if_pattern, replace_if, result, flags=re.DOTALL)

        # 处理 for 循环：{{#each items}}{{this}}{{/each}}
        each_pattern = r'\{\{#each (\w+)\}\}(.*?)\{\{/each\}\}'

        def replace_each(match):
            var_name = match.group(1)
            content = match.group(2)
            items = kwargs.get(var_name, [])
            return '\\n'.join(content.replace('{{this}}', str(item)) for item in items)

        result = re.sub(each_pattern, replace_each, result, flags=re.DOTALL)

        return result.format(**kwargs)
```

### 模块化模板组合

```python
class ModularTemplate:
    def __init__(self):
        self.components = {}

    def register_component(self, name, template):
        self.components[name] = template

    def render(self, structure, **kwargs):
        parts = []
        for component_name in structure:
            if component_name in self.components:
                component = self.components[component_name]
                parts.append(component.format(**kwargs))

        return '\\n\\n'.join(parts)

builder = ModularTemplate()
builder.register_component('system', "You are a {role}.")
builder.register_component('context', "Context: {context}")
builder.register_component('instruction', "Task: {task}")
builder.register_component('examples', "Examples:\\n{examples}")
builder.register_component('input', "Input: {input}")
builder.register_component('format', "Output format: {format}")
```

## 常见模板模式

### 分类模板

```python
CLASSIFICATION_TEMPLATE = """
Classify the following {content_type} into one of these categories: {categories}

{{#if description}}
Category descriptions:
{description}
{{/if}}

{{#if examples}}
Examples:
{examples}
{{/if}}

{content_type}: {input}

Category:"""
```

### 抽取模板

```python
EXTRACTION_TEMPLATE = """
Extract structured information from the {content_type}.

Required fields:
{field_definitions}

{{#if examples}}
Example extraction:
{examples}
{{/if}}

{content_type}: {input}

Extracted information (JSON):"""
```

### 生成模板

```python
GENERATION_TEMPLATE = """
Generate {output_type} based on the following {input_type}.

Requirements:
{requirements}

{{#if style}}
Style: {style}
{{/if}}

{{#if constraints}}
Constraints:
{constraints}
{{/if}}

{{#if examples}}
Examples:
{examples}
{{/if}}

{input_type}: {input}

{output_type}:"""
```

### 转换模板

```python
TRANSFORMATION_TEMPLATE = """
Transform the input {source_format} to {target_format}.

Transformation rules:
{rules}

{{#if examples}}
Example transformations:
{examples}
{{/if}}

Input {source_format}:
{input}

Output {target_format}:"""
```

## 高级功能

### 模板继承

通过父模板定义通用片段，子模板只覆盖差异部分。适合让多个任务共享系统角色、输出格式或安全约束。

### 变量校验

渲染前校验变量类型、范围和枚举值，能提前发现错误，避免把不合法输入传给模型。

### 模板缓存

当模板变量稳定且渲染成本较高时，可以缓存渲染结果。动态用户输入不应盲目缓存，避免复用错误上下文。

## 多轮模板

### 对话模板

```python
class ConversationTemplate:
    def __init__(self, system_prompt):
        self.system_prompt = system_prompt
        self.history = []

    def add_user_message(self, message):
        self.history.append({'role': 'user', 'content': message})

    def add_assistant_message(self, message):
        self.history.append({'role': 'assistant', 'content': message})

    def render_for_api(self):
        messages = [{'role': 'system', 'content': self.system_prompt}]
        messages.extend(self.history)
        return messages

    def render_as_text(self):
        result = f"System: {self.system_prompt}\\n\\n"
        for msg in self.history:
            role = msg['role'].capitalize()
            result += f"{role}: {msg['content']}\\n\\n"
        return result
```

### 基于状态的模板

基于状态的模板适合多步骤工作流。每个状态注册一段模板，渲染时根据 `current_state` 选择当前阶段提示词。

## 最佳实践

1. **保持 DRY**：用模板避免重复
2. **尽早校验**：渲染前检查变量
3. **版本化模板**：像代码一样跟踪修改
4. **测试变体**：确保模板能处理多样输入
5. **记录变量**：清楚说明必填和可选变量
6. **使用类型提示**：明确变量类型
7. **提供默认值**：为合适位置设置合理默认值
8. **谨慎缓存**：缓存静态模板，不缓存高度动态内容

## 模板库

### 问答

```python
QA_TEMPLATES = {
    'factual': """Answer the question based on the context.

Context: {context}
Question: {question}
Answer:""",

    'multi_hop': """Answer the question by reasoning across multiple facts.

Facts: {facts}
Question: {question}

Reasoning:""",

    'conversational': """Continue the conversation naturally.

Previous conversation:
{history}

User: {question}
Assistant:"""
}
```

### 内容生成

```python
GENERATION_TEMPLATES = {
    'blog_post': """Write a blog post about {topic}.

Requirements:
- Length: {word_count} words
- Tone: {tone}
- Include: {key_points}

Blog post:""",

    'product_description': """Write a product description for {product}.

Features: {features}
Benefits: {benefits}
Target audience: {audience}

Description:""",

    'email': """Write a {type} email.

To: {recipient}
Context: {context}
Key points: {key_points}

Email:"""
}
```

## 性能注意事项

- 对重复使用的模板进行预编译
- 变量静态时缓存渲染结果
- 尽量减少循环中的字符串拼接
- 使用高效字符串格式化，例如 f-string 或 `.format()`
- 对模板渲染热点做性能分析
