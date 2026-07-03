#!/usr/bin/env python3
"""列出 GitHub 仓库路径下的 skills，并标记当前应用中已安装的项。"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error

from pathlib import Path

from github_utils import github_api_contents_url, github_request

DEFAULT_REPO = "openai/skills"
DEFAULT_PATH = "skills/.curated"
DEFAULT_REF = "main"


class ListError(Exception):
    pass


class Args(argparse.Namespace):
    repo: str
    path: str
    ref: str
    format: str


def _request(url: str) -> bytes:
    return github_request(url, "wesley-skill-list")


def _skills_root() -> str:
    configured = os.environ.get("WESLEY_SKILLS_ROOT")
    if configured:
        return configured

    for parent in Path(__file__).resolve().parents:
        if parent.name == "skills":
            return str(parent)

    return os.path.join(os.getcwd(), "skills")


def _installed_skills() -> set[str]:
    root = _skills_root()
    if not os.path.isdir(root):
        return set()
    entries = set()
    for name in os.listdir(root):
        path = os.path.join(root, name)
        if os.path.isdir(path):
            entries.add(name)
    system_root = os.path.join(root, "system")
    if os.path.isdir(system_root):
        for name in os.listdir(system_root):
            path = os.path.join(system_root, name)
            if os.path.isdir(path):
                entries.add(name)
    return entries


def _list_skills(repo: str, path: str, ref: str) -> list[str]:
    api_url = github_api_contents_url(repo, path, ref)
    try:
        payload = _request(api_url)
    except urllib.error.HTTPError as exc:
        if exc.code == 404:
            raise ListError(
                "未找到 skills 路径："
                f"https://github.com/{repo}/tree/{ref}/{path}"
            ) from exc
        raise ListError(f"获取 skills 列表失败：HTTP {exc.code}") from exc
    data = json.loads(payload.decode("utf-8"))
    if not isinstance(data, list):
        raise ListError("skills 列表响应格式不符合预期。")
    skills = [item["name"] for item in data if item.get("type") == "dir"]
    return sorted(skills)


def _parse_args(argv: list[str]) -> Args:
    parser = argparse.ArgumentParser(description="列出可安装的 skills。")
    parser.add_argument("--repo", default=DEFAULT_REPO)
    parser.add_argument(
        "--path",
        default=DEFAULT_PATH,
        help="要列出的仓库路径（默认：skills/.curated）",
    )
    parser.add_argument("--ref", default=DEFAULT_REF)
    parser.add_argument(
        "--format",
        choices=["text", "json"],
        default="text",
        help="输出格式",
    )
    return parser.parse_args(argv, namespace=Args())


def main(argv: list[str]) -> int:
    args = _parse_args(argv)
    try:
        skills = _list_skills(args.repo, args.path, args.ref)
        installed = _installed_skills()
        if args.format == "json":
            payload = [
                {"name": name, "installed": name in installed} for name in skills
            ]
            print(json.dumps(payload, ensure_ascii=False))
        else:
            for idx, name in enumerate(skills, start=1):
                suffix = "（已安装）" if name in installed else ""
                print(f"{idx}. {name}{suffix}")
        return 0
    except ListError as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
