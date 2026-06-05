# Few-Shot 学习指南

## 概览

Few-shot 学习通过在提示词中提供少量示例（通常 1-10 个），让 LLM 学会执行任务。该技术非常适合要求特定格式、风格或领域知识的任务。

## 示例选择策略

### 1. 语义相似度

使用基于 embedding 的检索，选择与输入查询最相似的示例。

```python
from sentence_transformers import SentenceTransformer
import numpy as np

class SemanticExampleSelector:
    def __init__(self, examples, model_name='all-MiniLM-L6-v2'):
        self.model = SentenceTransformer(model_name)
        self.examples = examples
        self.example_embeddings = self.model.encode([ex['input'] for ex in examples])

    def select(self, query, k=3):
        query_embedding = self.model.encode([query])
        similarities = np.dot(self.example_embeddings, query_embedding.T).flatten()
        top_indices = np.argsort(similarities)[-k:][::-1]
        return [self.examples[i] for i in top_indices]
```

**最适合：** 问答、文本分类、抽取任务。

### 2. 多样性采样

最大化覆盖不同模式和边界情况。

```python
from sklearn.cluster import KMeans

class DiversityExampleSelector:
    def __init__(self, examples, model_name='all-MiniLM-L6-v2'):
        self.model = SentenceTransformer(model_name)
        self.examples = examples
        self.embeddings = self.model.encode([ex['input'] for ex in examples])

    def select(self, k=5):
        # 使用 k-means 查找多样化聚类中心
        kmeans = KMeans(n_clusters=k, random_state=42)
        kmeans.fit(self.embeddings)

        # 选择最接近每个聚类中心的示例
        diverse_examples = []
        for center in kmeans.cluster_centers_:
            distances = np.linalg.norm(self.embeddings - center, axis=1)
            closest_idx = np.argmin(distances)
            diverse_examples.append(self.examples[closest_idx])

        return diverse_examples
```

**最适合：** 展示任务变化、处理边界情况。

### 3. 基于难度选择

逐步增加示例复杂度，帮助模型建立任务脚手架。

```python
class ProgressiveExampleSelector:
    def __init__(self, examples):
        # examples 应包含 'difficulty' 分数（0-1）
        self.examples = sorted(examples, key=lambda x: x['difficulty'])

    def select(self, k=3):
        # 选择难度线性增加的示例
        step = len(self.examples) // k
        return [self.examples[i * step] for i in range(k)]
```

**最适合：** 复杂推理任务、代码生成。

### 4. 基于错误选择

加入能覆盖常见失败模式的示例。

```python
class ErrorGuidedSelector:
    def __init__(self, examples, error_patterns):
        self.examples = examples
        self.error_patterns = error_patterns  # 要避免的常见错误

    def select(self, query, k=3):
        # 选择展示如何正确处理错误模式的示例
        selected = []
        for pattern in self.error_patterns[:k]:
            matching = [ex for ex in self.examples if pattern in ex['demonstrates']]
            if matching:
                selected.append(matching[0])
        return selected
```

**最适合：** 已知失败模式的任务、安全关键应用。

## 示例构造最佳实践

### 格式一致

所有示例都应遵循相同格式：

```python
# 好：格式一致
examples = [
    {
        "input": "What is the capital of France?",
        "output": "Paris"
    },
    {
        "input": "What is the capital of Germany?",
        "output": "Berlin"
    }
]

# 差：格式不一致
examples = [
    "Q: What is the capital of France? A: Paris",
    {"question": "What is the capital of Germany?", "answer": "Berlin"}
]
```

### 输入输出对齐

确保示例展示的正是你希望模型执行的任务：

```python
# 好：输入输出关系清晰
example = {
    "input": "Sentiment: The movie was terrible and boring.",
    "output": "Negative"
}

# 差：关系含糊
example = {
    "input": "The movie was terrible and boring.",
    "output": "This review expresses negative sentiment toward the film."
}
```

### 复杂度平衡

加入覆盖预期难度范围的示例：

```python
examples = [
    {"input": "2 + 2", "output": "4"},
    {"input": "15 * 3 + 8", "output": "53"},
    {"input": "(12 + 8) * 3 - 15 / 5", "output": "57"}
]
```

## 上下文窗口管理

### Token 预算分配

4K 上下文窗口的典型分配：

```text
系统提示词：       500 tokens  (12%)
Few-shot 示例：  1500 tokens  (38%)
用户输入：         500 tokens  (12%)
响应：            1500 tokens  (38%)
```

### 动态示例截断

```python
class TokenAwareSelector:
    def __init__(self, examples, tokenizer, max_tokens=1500):
        self.examples = examples
        self.tokenizer = tokenizer
        self.max_tokens = max_tokens

    def select(self, query, k=5):
        selected = []
        total_tokens = 0

        candidates = self.rank_by_relevance(query)

        for example in candidates[:k]:
            example_tokens = len(self.tokenizer.encode(
                f"Input: {example['input']}\nOutput: {example['output']}\n\n"
            ))

            if total_tokens + example_tokens <= self.max_tokens:
                selected.append(example)
                total_tokens += example_tokens
            else:
                break

        return selected
```

## 边界情况处理

### 加入边界示例

```python
edge_case_examples = [
    {"input": "", "output": "Please provide input text."},
    {"input": "..." + "word " * 1000, "output": "Input exceeds maximum length."},
    {"input": "bank", "output": "Ambiguous: Could refer to financial institution or river bank."},
    {"input": "!@#$%", "output": "Invalid input format. Please provide valid text."}
]
```

## Few-Shot 提示词模板

### 分类模板

```python
def build_classification_prompt(examples, query, labels):
    prompt = f"Classify the text into one of these categories: {', '.join(labels)}\n\n"

    for ex in examples:
        prompt += f"Text: {ex['input']}\nCategory: {ex['output']}\n\n"

    prompt += f"Text: {query}\nCategory:"
    return prompt
```

### 抽取模板

```python
def build_extraction_prompt(examples, query):
    prompt = "Extract structured information from the text.\n\n"

    for ex in examples:
        prompt += f"Text: {ex['input']}\nExtracted: {json.dumps(ex['output'])}\n\n"

    prompt += f"Text: {query}\nExtracted:"
    return prompt
```

### 转换模板

```python
def build_transformation_prompt(examples, query):
    prompt = "Transform the input according to the pattern shown in examples.\n\n"

    for ex in examples:
        prompt += f"Input: {ex['input']}\nOutput: {ex['output']}\n\n"

    prompt += f"Input: {query}\nOutput:"
    return prompt
```

## 评估与优化

### 示例质量指标

```python
def evaluate_example_quality(example, validation_set):
    metrics = {
        'clarity': rate_clarity(example),
        'representativeness': calculate_similarity_to_validation(example, validation_set),
        'difficulty': estimate_difficulty(example),
        'uniqueness': calculate_uniqueness(example, other_examples)
    }
    return metrics
```

### A/B 测试示例集

```python
class ExampleSetTester:
    def __init__(self, llm_client):
        self.client = llm_client

    def compare_example_sets(self, set_a, set_b, test_queries):
        results_a = self.evaluate_set(set_a, test_queries)
        results_b = self.evaluate_set(set_b, test_queries)

        return {
            'set_a_accuracy': results_a['accuracy'],
            'set_b_accuracy': results_b['accuracy'],
            'winner': 'A' if results_a['accuracy'] > results_b['accuracy'] else 'B',
            'improvement': abs(results_a['accuracy'] - results_b['accuracy'])
        }
```

## 高级技术

### 元学习：学习如何选择

训练一个小模型来预测哪些示例最有效。

### 自适应示例数量

根据任务难度动态调整示例数量。先从少量示例开始，如果置信度不足再逐步增加，避免不必要的上下文消耗。

## 常见错误

1. **示例过多**：更多不一定更好，可能稀释重点
2. **示例无关**：示例应紧密匹配目标任务
3. **格式不一致**：会让模型困惑输出格式
4. **过拟合示例**：模型过于机械地复制示例模式
5. **忽略 token 限制**：导致实际输入或输出空间不足

## 资源

- 示例数据集仓库
- 常见任务的预构建示例选择器
- Few-shot 性能评估框架
- 不同模型的 token 计数工具
