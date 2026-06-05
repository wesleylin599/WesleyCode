---
name: prompt-engineering-patterns
description: 掌握高级提示词工程技术，以提升生产环境中 LLM 的性能、可靠性和可控性。当需要优化提示词、改进 LLM 输出，或设计生产级提示词模板时使用。
---

# 提示词工程模式

掌握高级提示词工程技术，用于最大化 LLM 的性能、可靠性和可控性。

## 何时使用本技能

- 为生产级 LLM 应用设计复杂提示词
- 优化提示词性能与输出一致性
- 实现结构化推理模式，如思维链、思维树
- 构建带动态示例选择的 few-shot 学习系统
- 创建支持变量插值的可复用提示词模板
- 调试和改进输出不稳定的提示词
- 为专业 AI 助手实现系统提示词
- 使用结构化输出（JSON mode）以便可靠解析

## 核心能力

### 1. Few-Shot 学习

- 示例选择策略：语义相似度、多样性采样
- 在示例数量与上下文窗口限制之间取舍
- 使用输入-输出对构建有效示范
- 从知识库动态检索示例
- 通过策略性示例选择处理边界情况

### 2. 思维链提示

- 引导逐步推理
- 使用 "Let's think step by step" 做零样本 CoT
- 使用带推理轨迹的 few-shot CoT
- 自一致性技术：采样多条推理路径
- 验证与校验步骤

### 3. 结构化输出

- 使用 JSON mode 可靠解析
- 使用 Pydantic 约束 schema
- 类型安全地处理响应
- 处理格式错误的输出

### 4. 提示词优化

- 迭代式改进工作流
- A/B 测试提示词变体
- 衡量准确率、一致性、延迟等指标
- 在保持质量的同时减少 token 使用
- 处理边界情况和失败模式

### 5. 模板系统

- 变量插值与格式化
- 条件提示词片段
- 多轮对话模板
- 基于角色的提示词组合
- 模块化提示词组件

### 6. 系统提示词设计

- 设定模型行为和约束
- 定义输出格式和结构
- 建立角色与专业能力
- 安全指南与内容政策
- 设置上下文和背景信息

## 快速开始

```python
from langchain_anthropic import ChatAnthropic
from langchain_core.prompts import ChatPromptTemplate
from pydantic import BaseModel, Field

# 定义结构化输出 schema
class SQLQuery(BaseModel):
    query: str = Field(description="The SQL query")
    explanation: str = Field(description="Brief explanation of what the query does")
    tables_used: list[str] = Field(description="List of tables referenced")

# 初始化支持结构化输出的模型
llm = ChatAnthropic(model="claude-sonnet-4-6")
structured_llm = llm.with_structured_output(SQLQuery)

# 创建提示词模板
prompt = ChatPromptTemplate.from_messages([
    ("system", """You are an expert SQL developer. Generate efficient, secure SQL queries.
    Always use parameterized queries to prevent SQL injection.
    Explain your reasoning briefly."""),
    ("user", "Convert this to SQL: {query}")
])

# 创建链
chain = prompt | structured_llm

# 使用
result = await chain.ainvoke({
    "query": "Find all users who registered in the last 30 days"
})
print(result.query)
print(result.explanation)
```

## 详细模式与完整示例

详细模式文档位于 `references/details.md`。当上面的导航层不足以完成任务时，读取该文件。

## 最佳实践

1. **具体明确**：模糊提示词会导致输出不稳定
2. **用示例说明**：示例通常比文字描述更有效
3. **使用结构化输出**：用 Pydantic 等 schema 提高可靠性
4. **充分测试**：使用多样且有代表性的输入评估
5. **快速迭代**：小改动可能带来大影响
6. **监控性能**：在生产中跟踪指标
7. **版本控制**：像管理代码一样管理提示词
8. **记录意图**：说明提示词为何这样组织

## 常见问题

- **过度工程化**：尝试简单提示词之前就引入复杂结构
- **示例污染**：使用与目标任务不匹配的示例
- **上下文溢出**：示例过多导致超过 token 限制
- **指令歧义**：给模型留下多种解释空间
- **忽略边界情况**：未测试异常或临界输入
- **缺少错误处理**：假设输出永远格式正确
- **硬编码值**：未将提示词参数化以便复用

## 成功指标

为提示词跟踪这些 KPI：

- **准确率**：输出是否正确
- **一致性**：相似输入下是否可复现
- **延迟**：响应时间，例如 P50、P95、P99
- **Token 使用量**：每次请求平均 token 数
- **成功率**：有效且可解析输出的比例
- **用户满意度**：评分与反馈
