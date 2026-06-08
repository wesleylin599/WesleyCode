# 思维链提示

## 概览

Chain-of-Thought（CoT，思维链）提示会引导 LLM 进行逐步推理，能显著提升复杂推理、数学和逻辑任务的表现。

## 核心技术

### 零样本 CoT

添加一个简单触发短语来引导推理：

```python
def zero_shot_cot(query):
    return f"""{query}

Let's think step by step:"""

# 示例
query = "If a train travels 60 mph for 2.5 hours, how far does it go?"
prompt = zero_shot_cot(query)

# 模型输出：
# "Let's think step by step:
# 1. Speed = 60 miles per hour
# 2. Time = 2.5 hours
# 3. Distance = Speed * Time
# 4. Distance = 60 * 2.5 = 150 miles
# Answer: 150 miles"
```

### Few-Shot CoT

提供包含显式推理链的示例：

```python
few_shot_examples = """
Q: Roger has 5 tennis balls. He buys 2 more cans of tennis balls. Each can has 3 balls. How many tennis balls does he have now?
A: Let's think step by step:
1. Roger starts with 5 balls
2. He buys 2 cans, each with 3 balls
3. Balls from cans: 2 * 3 = 6 balls
4. Total: 5 + 6 = 11 balls
Answer: 11

Q: The cafeteria had 23 apples. If they used 20 to make lunch and bought 6 more, how many do they have?
A: Let's think step by step:
1. Started with 23 apples
2. Used 20 for lunch: 23 - 20 = 3 apples left
3. Bought 6 more: 3 + 6 = 9 apples
Answer: 9

Q: {user_query}
A: Let's think step by step:"""
```

### 自一致性

生成多条推理路径，并采用多数投票：

```python
import openai
from collections import Counter

def self_consistency_cot(query, n=5, temperature=0.7):
    prompt = f"{query}\n\nLet's think step by step:"

    responses = []
    for _ in range(n):
        response = openai.ChatCompletion.create(
            model="gpt-5.4",
            messages=[{"role": "user", "content": prompt}],
            temperature=temperature
        )
        responses.append(extract_final_answer(response))

    # 多数投票
    answer_counts = Counter(responses)
    final_answer = answer_counts.most_common(1)[0][0]

    return {
        'answer': final_answer,
        'confidence': answer_counts[final_answer] / n,
        'all_responses': responses
    }
```

## 高级模式

### 从少到多提示

把复杂问题拆成更简单的子问题：

```python
def least_to_most_prompt(complex_query):
    # 阶段 1：拆解
    decomp_prompt = f"""Break down this complex problem into simpler subproblems:

Problem: {complex_query}

Subproblems:"""

    subproblems = get_llm_response(decomp_prompt)

    # 阶段 2：顺序求解
    solutions = []
    context = ""

    for subproblem in subproblems:
        solve_prompt = f"""{context}

Solve this subproblem:
{subproblem}

Solution:"""
        solution = get_llm_response(solve_prompt)
        solutions.append(solution)
        context += f"\n\nPreviously solved: {subproblem}\nSolution: {solution}"

    # 阶段 3：最终整合
    final_prompt = f"""Given these solutions to subproblems:
{context}

Provide the final answer to: {complex_query}

Final Answer:"""

    return get_llm_response(final_prompt)
```

### 思维树（Tree-of-Thought）

探索多条推理分支：

```python
class TreeOfThought:
    def __init__(self, llm_client, max_depth=3, branches_per_step=3):
        self.client = llm_client
        self.max_depth = max_depth
        self.branches_per_step = branches_per_step

    def solve(self, problem):
        # 生成初始思考分支
        initial_thoughts = self.generate_thoughts(problem, depth=0)

        # 评估每个分支
        best_path = None
        best_score = -1

        for thought in initial_thoughts:
            path, score = self.explore_branch(problem, thought, depth=1)
            if score > best_score:
                best_score = score
                best_path = path

        return best_path
```

### 验证步骤

添加显式验证以捕获错误：

```python
def cot_with_verification(query):
    reasoning_prompt = f"""{query}

Let's solve this step by step:"""

    reasoning_response = get_llm_response(reasoning_prompt)

    verification_prompt = f"""Original problem: {query}

Proposed solution:
{reasoning_response}

Verify this solution by:
1. Checking each step for logical errors
2. Verifying arithmetic calculations
3. Ensuring the final answer makes sense

Is this solution correct? If not, what's wrong?

Verification:"""

    verification = get_llm_response(verification_prompt)

    if "incorrect" in verification.lower() or "error" in verification.lower():
        revision_prompt = f"""The previous solution had errors:
{verification}

Please provide a corrected solution to: {query}

Corrected solution:"""
        return get_llm_response(revision_prompt)

    return reasoning_response
```

## 领域化 CoT

### 数学问题

```python
math_cot_template = """
Problem: {problem}

Solution:
Step 1: Identify what we know
- {list_known_values}

Step 2: Identify what we need to find
- {target_variable}

Step 3: Choose relevant formulas
- {formulas}

Step 4: Substitute values
- {substitution}

Step 5: Calculate
- {calculation}

Step 6: Verify and state answer
- {verification}

Answer: {final_answer}
"""
```

### 代码调试

```python
debug_cot_template = """
Code with error:
{code}

Error message:
{error}

Debugging process:
Step 1: Understand the error message
- {interpret_error}

Step 2: Locate the problematic line
- {identify_line}

Step 3: Analyze why this line fails
- {root_cause}

Step 4: Determine the fix
- {proposed_fix}

Step 5: Verify the fix addresses the error
- {verification}

Fixed code:
{corrected_code}
"""
```

### 逻辑推理

```python
logic_cot_template = """
Premises:
{premises}

Question: {question}

Reasoning:
Step 1: List all given facts
{facts}

Step 2: Identify logical relationships
{relationships}

Step 3: Apply deductive reasoning
{deductions}

Step 4: Draw conclusion
{conclusion}

Answer: {final_answer}
"""
```

## 性能优化

### 缓存推理模式

```python
class ReasoningCache:
    def __init__(self):
        self.cache = {}

    def get_similar_reasoning(self, problem, threshold=0.85):
        problem_embedding = embed(problem)

        for cached_problem, reasoning in self.cache.items():
            similarity = cosine_similarity(
                problem_embedding,
                embed(cached_problem)
            )
            if similarity > threshold:
                return reasoning

        return None

    def add_reasoning(self, problem, reasoning):
        self.cache[problem] = reasoning
```

### 自适应推理深度

```python
def adaptive_cot(problem, initial_depth=3):
    depth = initial_depth

    while depth <= 10:  # 最大深度
        response = generate_cot(problem, num_steps=depth)

        if is_solution_complete(response):
            return response

        depth += 2

    return response
```

## 评估指标

```python
def evaluate_cot_quality(reasoning_chain):
    metrics = {
        'coherence': measure_logical_coherence(reasoning_chain),
        'completeness': check_all_steps_present(reasoning_chain),
        'correctness': verify_final_answer(reasoning_chain),
        'efficiency': count_unnecessary_steps(reasoning_chain),
        'clarity': rate_explanation_clarity(reasoning_chain)
    }
    return metrics
```

## 最佳实践

1. **清晰步骤标记**：使用编号步骤或明确分隔符
2. **展示完整过程**：不要跳过步骤，即使看起来很明显
3. **验证计算**：加入显式验证步骤
4. **声明假设**：把隐含假设说清楚
5. **检查边界情况**：考虑临界条件
6. **使用示例**：先展示推理模式示例

## 常见问题

- **过早下结论**：未完整推理就直接给答案
- **循环论证**：用结论反过来证明推理
- **缺少步骤**：跳过中间计算
- **过度复杂**：加入不必要步骤导致混乱
- **格式不一致**：推理过程中改变步骤结构

## 何时使用 CoT

**适合使用 CoT：**

- 数学和算术问题
- 逻辑推理任务
- 多步骤规划
- 代码生成和调试
- 复杂决策

**不适合使用 CoT：**

- 简单事实查询
- 直接查找
- 创意写作
- 要求极简答案的任务
- 实时、延迟敏感的应用

## 资源

- CoT 评估基准数据集
- 预构建 CoT 提示词模板
- 推理验证工具
- 步骤提取与解析工具
