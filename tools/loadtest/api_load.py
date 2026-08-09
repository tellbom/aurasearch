#!/usr/bin/env python3
"""API-only data generation, dual-write verification, and search load testing."""

from __future__ import annotations

import argparse
import asyncio
import json
import math
import random
import statistics
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timedelta, timezone
from typing import Any


TOPICS = (
    "人工智能产业",
    "新能源技术",
    "数字经济发展",
    "公司年度公告",
    "金融市场动态",
    "先进制造项目",
    "交通基础设施",
    "医疗健康服务",
    "教育数字平台",
    "企业经营报告",
)
EVENTS = (
    "发布最新进展",
    "完成阶段验收",
    "公布运营数据",
    "启动内部试点",
    "披露季度成果",
)
PUBLISHERS = ("内网日报", "交易所", "技术门户", "行业资讯", "企业公告平台")
AUTHORS = ("张三", "李四", "王五", "赵六", "编辑部")
SOURCE_TYPES = ("News", "Announcement", "Portal")
BASE_TIME = datetime(2025, 1, 1, tzinfo=timezone.utc)
ALPHABET = "0123456789abcdefghijklmnopqrstuvwxyz"
MARKER_SPACE = len(ALPHABET) ** 10
MARKER_MULTIPLIER = 2_654_435_761


def marker(index: int) -> str:
    value = ((index + 1) * MARKER_MULTIPLIER) % MARKER_SPACE
    encoded = []
    for _ in range(10):
        value, remainder = divmod(value, len(ALPHABET))
        encoded.append(ALPHABET[remainder])
    return "lx" + "".join(reversed(encoded))


def news_id(index: int) -> str:
    return f"load-{index:06d}"


def make_document(index: int) -> dict[str, Any]:
    unique = marker(index)
    topic = TOPICS[index % len(TOPICS)]
    event = EVENTS[(index // len(TOPICS)) % len(EVENTS)]
    publisher = PUBLISHERS[(index // 7) % len(PUBLISHERS)]
    author = AUTHORS[(index // 11) % len(AUTHORS)]
    source_type = SOURCE_TYPES[index % len(SOURCE_TYPES)]
    published = BASE_TIME + timedelta(minutes=index * 17)
    return {
        "newsId": news_id(index),
        "document": {
            "sourceId": f"source-{index:06d}",
            "sourceType": source_type,
            "title": f"{topic}{event} {unique}",
            "contentHtml": (
                f"<article><p>{topic}{event}，编号 {unique}。</p>"
                f"<p>本条合成数据用于内网双引擎检索压力测试，发布渠道为{publisher}。</p></article>"
            ),
            "publisher": publisher,
            "author": author,
            "publishTime": published.isoformat().replace("+00:00", "Z"),
            "indexVersion": 1,
        },
    }


def request_json(
    method: str,
    url: str,
    payload: dict[str, Any] | None = None,
    timeout: float = 30,
) -> tuple[int, Any, float]:
    data = None if payload is None else json.dumps(
        payload, ensure_ascii=False, separators=(",", ":")
    ).encode("utf-8")
    request = urllib.request.Request(
        url,
        data=data,
        method=method,
        headers={"Content-Type": "application/json"},
    )
    started = time.perf_counter()
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            body = response.read()
            status = response.status
    except urllib.error.HTTPError as error:
        body = error.read()
        status = error.code
    elapsed_ms = (time.perf_counter() - started) * 1000
    parsed = json.loads(body) if body else None
    return status, parsed, elapsed_ms


def percentile(samples: list[float], quantile: float) -> float:
    if not samples:
        return 0.0
    ordered = sorted(samples)
    position = (len(ordered) - 1) * quantile
    lower = math.floor(position)
    upper = math.ceil(position)
    if lower == upper:
        return ordered[lower]
    return ordered[lower] + (ordered[upper] - ordered[lower]) * (position - lower)


def latency_summary(samples: list[float]) -> dict[str, float]:
    return {
        "minMs": round(min(samples), 3) if samples else 0,
        "meanMs": round(statistics.fmean(samples), 3) if samples else 0,
        "p50Ms": round(percentile(samples, 0.50), 3),
        "p95Ms": round(percentile(samples, 0.95), 3),
        "p99Ms": round(percentile(samples, 0.99), 3),
        "maxMs": round(max(samples), 3) if samples else 0,
    }


def batches(total: int, size: int):
    for start in range(0, total, size):
        yield start, min(total, start + size)


def ingest_batch(api_url: str, start: int, end: int, timeout: float) -> dict[str, Any]:
    payload = {"documents": [make_document(index) for index in range(start, end)]}
    status, response, elapsed_ms = request_json(
        "POST",
        f"{api_url}/api/v1/index/documents/batch",
        payload,
        timeout,
    )
    statuses = Counter()
    if isinstance(response, list):
        statuses.update(item.get("status", "Missing") for item in response)
    return {
        "start": start,
        "end": end,
        "httpStatus": status,
        "itemStatuses": statuses,
        "responseItems": len(response) if isinstance(response, list) else 0,
        "latencyMs": elapsed_ms,
        "error": None if status == 202 else response,
    }


def command_ingest(args: argparse.Namespace) -> int:
    started = time.perf_counter()
    results = []
    ranges = list(batches(args.documents, args.batch_size))
    with ThreadPoolExecutor(max_workers=args.parallel_batches) as executor:
        futures = {
            executor.submit(ingest_batch, args.api_url, start, end, args.timeout): (start, end)
            for start, end in ranges
        }
        completed = 0
        for future in as_completed(futures):
            result = future.result()
            results.append(result)
            completed += 1
            if completed % 10 == 0 or completed == len(ranges):
                accepted = sum(item["itemStatuses"]["Accepted"] for item in results)
                print(
                    f"ingest batches={completed}/{len(ranges)} accepted={accepted}",
                    file=sys.stderr,
                    flush=True,
                )

    results.sort(key=lambda item: item["start"])
    elapsed = time.perf_counter() - started
    status_counts = Counter()
    http_counts = Counter()
    failures = []
    latencies = []
    response_items = 0
    for result in results:
        status_counts.update(result["itemStatuses"])
        http_counts[result["httpStatus"]] += 1
        response_items += result["responseItems"]
        latencies.append(result["latencyMs"])
        if result["error"] is not None or result["responseItems"] != result["end"] - result["start"]:
            failures.append(result)
    report = {
        "operation": "ingest",
        "documentsRequested": args.documents,
        "responseItems": response_items,
        "batchSize": args.batch_size,
        "parallelBatches": args.parallel_batches,
        "elapsedSeconds": round(elapsed, 3),
        "documentsPerSecond": round(args.documents / elapsed, 3),
        "httpStatusCounts": dict(http_counts),
        "itemStatusCounts": dict(status_counts),
        "batchLatency": latency_summary(latencies),
        "failures": failures[:5],
    }
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not failures and status_counts["Accepted"] == args.documents else 1


def command_wait_sync(args: argparse.Namespace) -> int:
    started = time.perf_counter()
    deadline = started + args.timeout
    last = None
    while time.perf_counter() < deadline:
        status, snapshot, elapsed_ms = request_json(
            "GET", f"{args.api_url}/api/v1/operations/indexing-snapshot", timeout=30
        )
        if status != 200:
            print(json.dumps({"httpStatus": status, "response": snapshot}, ensure_ascii=False))
            return 1
        last = snapshot
        desired = snapshot["desiredUpserts"]
        es_applied = snapshot["elasticsearchApplied"]
        vespa_applied = snapshot["vespaApplied"]
        backlog = snapshot["outboxBacklog"]
        age = time.perf_counter() - started
        print(
            f"sync elapsed={age:.1f}s desired={desired} es={es_applied} "
            f"vespa={vespa_applied} backlog={backlog} apiMs={elapsed_ms:.1f}",
            file=sys.stderr,
            flush=True,
        )
        if (
            desired == args.documents
            and es_applied == args.documents
            and vespa_applied == args.documents
            and backlog == 0
        ):
            print(json.dumps({
                "operation": "wait-sync",
                "elapsedSeconds": round(age, 3),
                "snapshot": snapshot,
            }, ensure_ascii=False, indent=2))
            return 0
        time.sleep(args.poll_seconds)
    print(json.dumps({
        "operation": "wait-sync",
        "timedOut": True,
        "elapsedSeconds": round(time.perf_counter() - started, 3),
        "snapshot": last,
    }, ensure_ascii=False, indent=2))
    return 1


def set_mode(api_url: str, mode: str) -> None:
    status, response, _ = request_json(
        "POST",
        f"{api_url}/api/v1/search-health/mode",
        {"mode": mode, "operator": "api-loadtest", "reason": f"verify {mode} load path"},
    )
    if status != 200:
        raise RuntimeError(f"mode change failed: HTTP {status}: {response}")


def run_search(api_url: str, index: int, timeout: float) -> dict[str, Any]:
    expected = news_id(index)
    started = time.perf_counter()
    try:
        status, response, _ = request_json(
            "POST",
            f"{api_url}/api/v1/search",
            {"query": marker(index), "page": 1, "pageSize": 10},
            timeout,
        )
        elapsed_ms = (time.perf_counter() - started) * 1000
        results = response.get("results", []) if isinstance(response, dict) else []
        ids = [item.get("newsId") for item in results]
        return {
            "status": status,
            "latencyMs": elapsed_ms,
            "found": expected in ids,
            "top1": bool(ids) and ids[0] == expected,
            "degraded": bool(response.get("degraded")) if isinstance(response, dict) else False,
            "responseMode": response.get("searchMode") if isinstance(response, dict) else None,
            "expected": expected,
            "returned": ids[:10],
            "error": None,
        }
    except Exception as error:  # load-test evidence must retain transport failures
        return {
            "status": 0,
            "latencyMs": (time.perf_counter() - started) * 1000,
            "found": False,
            "top1": False,
            "degraded": False,
            "responseMode": None,
            "expected": expected,
            "returned": [],
            "error": f"{type(error).__name__}: {error}",
        }


def command_accuracy(args: argparse.Namespace) -> int:
    set_mode(args.api_url, args.mode)
    randomizer = random.Random(args.seed)
    indices = randomizer.sample(range(args.documents), args.samples)
    started = time.perf_counter()
    with ThreadPoolExecutor(max_workers=args.concurrency) as executor:
        results = list(executor.map(
            lambda index: run_search(args.api_url, index, args.timeout),
            indices,
        ))
    elapsed = time.perf_counter() - started
    status_counts = Counter(result["status"] for result in results)
    failures = [result for result in results if result["status"] != 200 or not result["found"]]
    report = {
        "operation": "accuracy",
        "mode": args.mode,
        "samples": args.samples,
        "concurrency": args.concurrency,
        "elapsedSeconds": round(elapsed, 3),
        "requestsPerSecond": round(args.samples / elapsed, 3),
        "httpStatusCounts": dict(status_counts),
        "found": sum(result["found"] for result in results),
        "foundRate": round(sum(result["found"] for result in results) / args.samples, 6),
        "top1": sum(result["top1"] for result in results),
        "top1Rate": round(sum(result["top1"] for result in results) / args.samples, 6),
        "degraded": sum(result["degraded"] for result in results),
        "latency": latency_summary([result["latencyMs"] for result in results]),
        "failures": failures[:5],
    }
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not failures else 1


def decode_chunked(body: bytes) -> bytes:
    decoded = bytearray()
    position = 0
    while position < len(body):
        line_end = body.find(b"\r\n", position)
        if line_end < 0:
            break
        size_text = body[position:line_end].split(b";", 1)[0]
        size = int(size_text, 16)
        position = line_end + 2
        if size == 0:
            break
        decoded.extend(body[position:position + size])
        position += size + 2
    return bytes(decoded)


async def raw_search(
    host: str,
    port: int,
    path: str,
    index: int,
    semaphore: asyncio.Semaphore,
    start_event: asyncio.Event,
    timeout: float,
) -> dict[str, Any]:
    await start_event.wait()
    expected = news_id(index)
    payload = json.dumps(
        {"query": marker(index), "page": 1, "pageSize": 10},
        separators=(",", ":"),
    ).encode("utf-8")
    request = (
        f"POST {path} HTTP/1.1\r\nHost: {host}\r\n"
        "Content-Type: application/json\r\nConnection: close\r\n"
        f"Content-Length: {len(payload)}\r\n\r\n"
    ).encode("ascii") + payload
    async with semaphore:
        started = time.perf_counter()
        writer = None
        try:
            reader, writer = await asyncio.wait_for(
                asyncio.open_connection(host, port), timeout=timeout
            )
            writer.write(request)
            await writer.drain()
            raw = await asyncio.wait_for(reader.read(), timeout=timeout)
            elapsed_ms = (time.perf_counter() - started) * 1000
            headers_raw, body = raw.split(b"\r\n\r\n", 1)
            header_lines = headers_raw.split(b"\r\n")
            status = int(header_lines[0].split()[1])
            headers = {}
            for line in header_lines[1:]:
                key, value = line.split(b":", 1)
                headers[key.strip().lower()] = value.strip().lower()
            if headers.get(b"transfer-encoding") == b"chunked":
                body = decode_chunked(body)
            response = json.loads(body) if body else None
            results = response.get("results", []) if isinstance(response, dict) else []
            ids = [item.get("newsId") for item in results]
            return {
                "status": status,
                "latencyMs": elapsed_ms,
                "found": expected in ids,
                "top1": bool(ids) and ids[0] == expected,
                "degraded": bool(response.get("degraded")) if isinstance(response, dict) else False,
                "responseMode": response.get("searchMode") if isinstance(response, dict) else None,
                "expected": expected,
                "returned": ids[:10],
                "error": None,
            }
        except Exception as error:  # load-test evidence must retain transport failures
            return {
                "status": 0,
                "latencyMs": (time.perf_counter() - started) * 1000,
                "found": False,
                "top1": False,
                "degraded": False,
                "responseMode": None,
                "expected": expected,
                "returned": [],
                "error": f"{type(error).__name__}: {error}",
            }
        finally:
            if writer is not None:
                writer.close()


async def execute_load(args: argparse.Namespace) -> dict[str, Any]:
    parsed = urllib.parse.urlparse(args.api_url)
    host = parsed.hostname or "localhost"
    port = parsed.port or 80
    path = (parsed.path.rstrip("/") if parsed.path else "") + "/api/v1/search"
    semaphore = asyncio.Semaphore(args.concurrency)
    start_event = asyncio.Event()
    tasks = [
        asyncio.create_task(raw_search(
            host,
            port,
            path,
            (request_index * 7_919) % args.documents,
            semaphore,
            start_event,
            args.timeout,
        ))
        for request_index in range(args.requests)
    ]
    await asyncio.sleep(0.1)
    started = time.perf_counter()
    start_event.set()
    results = await asyncio.gather(*tasks)
    elapsed = time.perf_counter() - started
    status_counts = Counter(result["status"] for result in results)
    mode_counts = Counter(result["responseMode"] or "none" for result in results)
    errors = Counter(result["error"] or "none" for result in results)
    failures = [result for result in results if result["status"] != 200 or not result["found"]]
    return {
        "operation": "load",
        "mode": args.mode,
        "requests": args.requests,
        "concurrency": args.concurrency,
        "elapsedSeconds": round(elapsed, 3),
        "requestsPerSecond": round(args.requests / elapsed, 3),
        "httpStatusCounts": dict(status_counts),
        "responseModeCounts": dict(mode_counts),
        "found": sum(result["found"] for result in results),
        "foundRate": round(sum(result["found"] for result in results) / args.requests, 6),
        "top1": sum(result["top1"] for result in results),
        "top1Rate": round(sum(result["top1"] for result in results) / args.requests, 6),
        "degraded": sum(result["degraded"] for result in results),
        "latency": latency_summary([result["latencyMs"] for result in results]),
        "errorCounts": dict(errors),
        "failures": failures[:5],
    }


def command_load(args: argparse.Namespace) -> int:
    set_mode(args.api_url, args.mode)
    report = asyncio.run(execute_load(args))
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if report["httpStatusCounts"].get(200) == args.requests else 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    parser.add_argument("--api-url", default="http://dual-news-search:8080")
    subparsers = parser.add_subparsers(dest="command", required=True)

    ingest = subparsers.add_parser("ingest")
    ingest.add_argument("--documents", type=int, default=50_000)
    ingest.add_argument("--batch-size", type=int, default=200)
    ingest.add_argument("--parallel-batches", type=int, default=4)
    ingest.add_argument("--timeout", type=float, default=120)
    ingest.set_defaults(handler=command_ingest)

    wait_sync = subparsers.add_parser("wait-sync")
    wait_sync.add_argument("--documents", type=int, default=50_000)
    wait_sync.add_argument("--timeout", type=float, default=7_200)
    wait_sync.add_argument("--poll-seconds", type=float, default=10)
    wait_sync.set_defaults(handler=command_wait_sync)

    accuracy = subparsers.add_parser("accuracy")
    accuracy.add_argument("--documents", type=int, default=50_000)
    accuracy.add_argument("--mode", choices=("EsOnly", "VespaOnly", "Rrf"), required=True)
    accuracy.add_argument("--samples", type=int, default=1_000)
    accuracy.add_argument("--concurrency", type=int, default=32)
    accuracy.add_argument("--seed", type=int, default=20_260_809)
    accuracy.add_argument("--timeout", type=float, default=30)
    accuracy.set_defaults(handler=command_accuracy)

    load = subparsers.add_parser("load")
    load.add_argument("--documents", type=int, default=50_000)
    load.add_argument("--mode", choices=("EsOnly", "VespaOnly", "Rrf"), default="Rrf")
    load.add_argument("--requests", type=int, default=10_000)
    load.add_argument("--concurrency", type=int, default=256)
    load.add_argument("--timeout", type=float, default=30)
    load.set_defaults(handler=command_load)
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    args.api_url = args.api_url.rstrip("/")
    return args.handler(args)


if __name__ == "__main__":
    raise SystemExit(main())
