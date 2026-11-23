#!/usr/bin/env python3
import argparse
import concurrent.futures
import statistics
import time
import urllib.request


def fetch(url: str) -> float:
    started = time.perf_counter()
    with urllib.request.urlopen(url, timeout=5) as response:
        response.read()
        if response.status != 200:
            raise RuntimeError(f"Unexpected status {response.status}")
    return (time.perf_counter() - started) * 1000


def main() -> None:
    parser = argparse.ArgumentParser(description="Measure local health endpoint latency without containers.")
    parser.add_argument("--url", default="http://localhost:5080/health/ready")
    parser.add_argument("--requests", type=int, default=50)
    parser.add_argument("--concurrency", type=int, default=8)
    parser.add_argument("--p95-target-ms", type=int, default=500)
    args = parser.parse_args()
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrency) as pool:
        samples = list(pool.map(lambda _: fetch(args.url), range(args.requests)))
    samples.sort()
    p95 = samples[max(0, int(len(samples) * 0.95) - 1)]
    print(f"PASS: {len(samples)} requests, p50={statistics.median(samples):.1f}ms, p95={p95:.1f}ms")
    if p95 > args.p95_target_ms:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
