#!/usr/bin/env python3
"""
提示词优化脚本。

使用 A/B 测试和指标跟踪自动测试并优化提示词。
"""

import json
import time
from typing import List, Dict, Any
from dataclasses import dataclass
from concurrent.futures import ThreadPoolExecutor
import numpy as np


@dataclass
class TestCase:
    input: Dict[str, Any]
    expected_output: str
    metadata: Dict[str, Any] = None


class PromptOptimizer:
    def __init__(self, llm_client, test_suite: List[TestCase]):
        self.client = llm_client
        self.test_suite = test_suite
        self.results_history = []
        self.executor = ThreadPoolExecutor()

    def shutdown(self):
        """关闭线程池执行器。"""
        self.executor.shutdown(wait=True)

    def evaluate_prompt(self, prompt_template: str, test_cases: List[TestCase] = None) -> Dict[str, float]:
        """并行评估提示词模板在测试用例上的表现。"""
        if test_cases is None:
            test_cases = self.test_suite

        metrics = {
            'accuracy': [],
            'latency': [],
            'token_count': [],
            'success_rate': []
        }

        def process_test_case(test_case):
            start_time = time.time()

            # 使用测试用例输入渲染提示词
            prompt = prompt_template.format(**test_case.input)

            # 获取 LLM 响应
            response = self.client.complete(prompt)

            # 测量延迟
            latency = time.time() - start_time

            # 计算单个用例的指标
            token_count = len(prompt.split()) + len(response.split())
            success = 1 if response else 0
            accuracy = self.calculate_accuracy(response, test_case.expected_output)

            return {
                'latency': latency,
                'token_count': token_count,
                'success_rate': success,
                'accuracy': accuracy
            }

        # 并行运行测试用例
        results = list(self.executor.map(process_test_case, test_cases))

        # 聚合指标
        for result in results:
            metrics['latency'].append(result['latency'])
            metrics['token_count'].append(result['token_count'])
            metrics['success_rate'].append(result['success_rate'])
            metrics['accuracy'].append(result['accuracy'])

        return {
            'avg_accuracy': np.mean(metrics['accuracy']),
            'avg_latency': np.mean(metrics['latency']),
            'p95_latency': np.percentile(metrics['latency'], 95),
            'avg_tokens': np.mean(metrics['token_count']),
            'success_rate': np.mean(metrics['success_rate'])
        }

    def calculate_accuracy(self, response: str, expected: str) -> float:
        """计算响应与期望输出之间的准确率分数。"""
        # 简单精确匹配
        if response.strip().lower() == expected.strip().lower():
            return 1.0

        # 使用词重叠做部分匹配
        response_words = set(response.lower().split())
        expected_words = set(expected.lower().split())

        if not expected_words:
            return 0.0

        overlap = len(response_words & expected_words)
        return overlap / len(expected_words)

    def optimize(self, base_prompt: str, max_iterations: int = 5) -> Dict[str, Any]:
        """迭代优化提示词。"""
        current_prompt = base_prompt
        best_prompt = base_prompt
        best_score = 0
        current_metrics = None

        for iteration in range(max_iterations):
            print(f"\n第 {iteration + 1}/{max_iterations} 轮")

            # 评估当前提示词；若已有上一轮指标则避免重复评估
            if current_metrics:
                metrics = current_metrics
            else:
                metrics = self.evaluate_prompt(current_prompt)

            print(f"准确率: {metrics['avg_accuracy']:.2f}, 延迟: {metrics['avg_latency']:.2f}s")

            # 记录结果
            self.results_history.append({
                'iteration': iteration,
                'prompt': current_prompt,
                'metrics': metrics
            })

            # 如果有提升，则更新最佳结果
            if metrics['avg_accuracy'] > best_score:
                best_score = metrics['avg_accuracy']
                best_prompt = current_prompt

            # 达到目标后停止
            if metrics['avg_accuracy'] > 0.95:
                print("已达到目标准确率！")
                break

            # 为下一轮生成变体
            variations = self.generate_variations(current_prompt, metrics)

            # 测试变体并选择最佳项
            best_variation = current_prompt
            best_variation_score = metrics['avg_accuracy']
            best_variation_metrics = metrics

            for variation in variations:
                var_metrics = self.evaluate_prompt(variation)
                if var_metrics['avg_accuracy'] > best_variation_score:
                    best_variation_score = var_metrics['avg_accuracy']
                    best_variation = variation
                    best_variation_metrics = var_metrics

            current_prompt = best_variation
            current_metrics = best_variation_metrics

        return {
            'best_prompt': best_prompt,
            'best_score': best_score,
            'history': self.results_history
        }

    def generate_variations(self, prompt: str, current_metrics: Dict) -> List[str]:
        """生成待测试的提示词变体。"""
        variations = []

        # 变体 1：加入明确格式要求
        variations.append(prompt + "\n\n请用清晰、简洁的格式回答。")

        # 变体 2：加入逐步处理要求
        variations.append("请逐步解决这个问题。\n\n" + prompt)

        # 变体 3：加入验证步骤
        variations.append(prompt + "\n\n回答前请验证你的答案。")

        # 变体 4：压缩提示词
        concise = self.make_concise(prompt)
        if concise != prompt:
            variations.append(concise)

        # 变体 5：如果没有示例，则加入示例
        if "example" not in prompt.lower():
            variations.append(self.add_examples(prompt))

        return variations[:3]  # 返回前 3 个变体

    def make_concise(self, prompt: str) -> str:
        """删除冗余词，让提示词更简洁。"""
        replacements = [
            ("in order to", "to"),
            ("due to the fact that", "because"),
            ("at this point in time", "now"),
            ("in the event that", "if"),
        ]

        result = prompt
        for old, new in replacements:
            result = result.replace(old, new)

        return result

    def add_examples(self, prompt: str) -> str:
        """为提示词添加示例部分。"""
        return f"""{prompt}

示例：
输入：示例输入
输出：示例输出
"""

    def compare_prompts(self, prompt_a: str, prompt_b: str) -> Dict[str, Any]:
        """对两个提示词进行 A/B 测试。"""
        print("正在测试提示词 A...")
        metrics_a = self.evaluate_prompt(prompt_a)

        print("正在测试提示词 B...")
        metrics_b = self.evaluate_prompt(prompt_b)

        return {
            'prompt_a_metrics': metrics_a,
            'prompt_b_metrics': metrics_b,
            'winner': 'A' if metrics_a['avg_accuracy'] > metrics_b['avg_accuracy'] else 'B',
            'improvement': abs(metrics_a['avg_accuracy'] - metrics_b['avg_accuracy'])
        }

    def export_results(self, filename: str):
        """将优化结果导出为 JSON。"""
        with open(filename, 'w') as f:
            json.dump(self.results_history, f, indent=2)


def main():
    # 使用示例
    test_suite = [
        TestCase(
            input={'text': 'This movie was amazing!'},
            expected_output='Positive'
        ),
        TestCase(
            input={'text': 'Worst purchase ever.'},
            expected_output='Negative'
        ),
        TestCase(
            input={'text': 'It was okay, nothing special.'},
            expected_output='Neutral'
        )
    ]

    # 用于演示的模拟 LLM 客户端
    class MockLLMClient:
        def complete(self, prompt):
            # 模拟 LLM 响应
            if 'amazing' in prompt:
                return 'Positive'
            elif 'worst' in prompt.lower():
                return 'Negative'
            else:
                return 'Neutral'

    optimizer = PromptOptimizer(MockLLMClient(), test_suite)

    try:
        base_prompt = "判断以下文本的情感：{text}\n情感："

        results = optimizer.optimize(base_prompt)

        print("\n" + "="*50)
        print("优化完成！")
        print(f"最佳准确率: {results['best_score']:.2f}")
        print(f"最佳提示词:\n{results['best_prompt']}")

        optimizer.export_results('optimization_results.json')
    finally:
        optimizer.shutdown()


if __name__ == '__main__':
    main()
