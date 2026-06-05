# 提示词优化指南

## 系统化改进流程

### 1. 建立基线

```python
def establish_baseline(prompt, test_cases):
    results = {
        'accuracy': 0,
        'avg_tokens': 0,
        'avg_latency': 0,
        'success_rate': 0
    }

    for test_case in test_cases:
        response = llm.complete(prompt.format(**test_case['input']))

        results['accuracy'] += evaluate_accuracy(response, test_case['expected'])
        results['avg_tokens'] += count_tokens(response)
        results['avg_latency'] += measure_latency(response)
        results['success_rate'] += is_valid_response(response)

    n = len(test_cases)
    return {k: v/n for k, v in results.items()}
```

### 2. 迭代式改进工作流

```text
初始提示词 -> 测试 -> 分析失败 -> 改进 -> 测试 -> 重复
```

```python
class PromptOptimizer:
    def __init__(self, initial_prompt, test_suite):
        self.prompt = initial_prompt
        self.test_suite = test_suite
        self.history = []

    def optimize(self, max_iterations=10):
        for i in range(max_iterations):
            results = self.evaluate_prompt(self.prompt)
            self.history.append({
                'iteration': i,
                'prompt': self.prompt,
                'results': results
            })

            if results['accuracy'] > 0.95:
                break

            failures = self.analyze_failures(results)
            refinements = self.generate_refinements(failures)
            self.prompt = self.select_best_refinement(refinements)

        return self.get_best_prompt()
```

### 3. A/B 测试框架

```python
class PromptABTest:
    def __init__(self, variant_a, variant_b):
        self.variant_a = variant_a
        self.variant_b = variant_b

    def run_test(self, test_queries, metrics=['accuracy', 'latency']):
        results = {
            'A': {m: [] for m in metrics},
            'B': {m: [] for m in metrics}
        }

        for query in test_queries:
            variant = 'A' if random.random() < 0.5 else 'B'
            prompt = self.variant_a if variant == 'A' else self.variant_b

            response, metrics_data = self.execute_with_metrics(
                prompt.format(query=query['input'])
            )

            for metric in metrics:
                results[variant][metric].append(metrics_data[metric])

        return self.analyze_results(results)
```

## 优化策略

### 减少 Token

```python
def optimize_for_tokens(prompt):
    optimizations = [
        ('in order to', 'to'),
        ('due to the fact that', 'because'),
        ('at this point in time', 'now'),
        ('First, ...\\nThen, ...\\nFinally, ...', 'Steps: 1) ... 2) ... 3) ...'),
        ('Natural Language Processing (NLP)', 'NLP'),
        (' actually ', ' '),
        (' basically ', ' '),
        (' really ', ' ')
    ]

    optimized = prompt
    for old, new in optimizations:
        optimized = optimized.replace(old, new)

    return optimized
```

### 降低延迟

```python
def optimize_for_latency(prompt):
    strategies = {
        'shorter_prompt': reduce_token_count(prompt),
        'streaming': enable_streaming_response(prompt),
        'caching': add_cacheable_prefix(prompt),
        'early_stopping': add_stop_sequences(prompt)
    }

    best_strategy = None
    best_latency = float('inf')

    for name, modified_prompt in strategies.items():
        latency = measure_average_latency(modified_prompt)
        if latency < best_latency:
            best_latency = latency
            best_strategy = modified_prompt

    return best_strategy
```

### 提升准确率

```python
def improve_accuracy(prompt, failure_cases):
    improvements = []

    if has_format_errors(failure_cases):
        improvements.append("Output must be valid JSON with no additional text.")

    edge_cases = identify_edge_cases(failure_cases)
    if edge_cases:
        improvements.append(f"Examples of edge cases:\\n{format_examples(edge_cases)}")

    if has_logical_errors(failure_cases):
        improvements.append("Before responding, verify your answer is logically consistent.")

    if has_ambiguity_errors(failure_cases):
        improvements.append(clarify_ambiguous_instructions(prompt))

    return integrate_improvements(prompt, improvements)
```

## 性能指标

### 核心指标

```python
class PromptMetrics:
    @staticmethod
    def accuracy(responses, ground_truth):
        return sum(r == gt for r, gt in zip(responses, ground_truth)) / len(responses)

    @staticmethod
    def consistency(responses):
        # 衡量相同输入是否产生相同输出
        from collections import defaultdict
        input_responses = defaultdict(list)

        for inp, resp in responses:
            input_responses[inp].append(resp)

        consistency_scores = []
        for inp, resps in input_responses.items():
            if len(resps) > 1:
                most_common_count = Counter(resps).most_common(1)[0][1]
                consistency_scores.append(most_common_count / len(resps))

        return np.mean(consistency_scores) if consistency_scores else 1.0
```

### 自动化评估

对每个测试用例多次运行，以同时衡量准确率、一致性、延迟、token 使用和成功率。评估结果应输出平均准确率、平均一致性、P95 延迟、平均 token 和成功率。

## 失败分析

### 失败分类

```python
class FailureAnalyzer:
    def categorize_failures(self, test_results):
        categories = {
            'format_errors': [],
            'factual_errors': [],
            'logic_errors': [],
            'incomplete_responses': [],
            'hallucinations': [],
            'off_topic': []
        }

        for result in test_results:
            if not result['success']:
                category = self.determine_failure_type(
                    result['response'],
                    result['expected']
                )
                categories[category].append(result)

        return categories
```

常见修复方式：

- 格式错误：添加明确格式示例和约束
- 幻觉：加入“仅基于提供上下文回答”的 grounding 指令
- 回答不完整：要求覆盖问题的所有部分
- 逻辑错误：加入回答前验证步骤

## 版本管理与回滚

提示词应像代码一样保存版本，记录版本 ID、提示词内容、时间戳、指标、说明和父版本。回滚时使用指定版本的提示词；比较版本时输出提示词差异和指标变化。

## 最佳实践

1. **建立基线**：始终先测量初始表现
2. **一次只改一件事**：隔离变量，便于归因
3. **充分测试**：使用多样且有代表性的测试用例
4. **跟踪指标**：记录所有实验和结果
5. **验证显著性**：A/B 比较使用统计检验
6. **记录改动**：说明改了什么以及为什么改
7. **全部版本化**：支持回滚到旧版本
8. **监控生产环境**：持续评估已部署提示词

## 常见优化模式

### 模式 1：增加结构

```text
Before: "Analyze this text"
After: "Analyze this text for:\n1. Main topic\n2. Key arguments\n3. Conclusion"
```

### 模式 2：增加示例

```text
Before: "Extract entities"
After: "Extract entities\n\nExample:\nText: Apple released iPhone\nEntities: {company: Apple, product: iPhone}"
```

### 模式 3：增加约束

```text
Before: "Summarize this"
After: "Summarize in exactly 3 bullet points, 15 words each"
```

### 模式 4：增加验证

```text
Before: "Calculate..."
After: "Calculate... Then verify your calculation is correct before responding."
```

## 工具与实用程序

- 用于版本比较的提示词 diff 工具
- 自动化测试运行器
- 指标仪表盘
- A/B 测试框架
- Token 计数工具
- 延迟分析器
