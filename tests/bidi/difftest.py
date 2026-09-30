#!/usr/bin/env python3
"""Differential check of RtlFix.Core's UBA against python-bidi (reference implementation).

Usage:  python tests/bidi/difftest.py [--cases FILE] [--seed N] [--limit N]

Feeds the same lines to the C# probe (tools/RtlFix.Probe) and to bidi.get_display, then
reports every visual-order mismatch. Exits non-zero when at least one case differs.
"""
import argparse
import random
import subprocess
import sys
from pathlib import Path

from bidi import algorithm as bidi_ref

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / "tools" / "RtlFix.Probe" / "bin" / "Debug" / "net10.0" / "RtlFix.Probe.exe"

LATIN = list("abcdefghijklmnopqrstuvwxyz ABC") + ["PowerMonitor", "FullRGB.exe", "MainWindow.xaml.cs"]
ARABIC = list("سلام دنیا برنامه فارسی سیستم زمان")
PERSIAN = list("پچژگکیهدمراب")
DIGITS = list("0123456789") + list("۰۱۲۴۵۶۸۹")
NEUTRALS = list("()[]{}<>=+-*/.,:;!?_'\"|@#$%&") + ["G:\\", "://", "3.5", "1,000", "→", "×"]
CONTROLS = ["\u200f", "\u200e", "\u202b", "\u202c", "\u2066", "\u2067", "\u2068", "\u2069", "\u200d", "\u061c"]


def random_line(rng):
    parts = []
    for _ in range(rng.randint(2, 9)):
        kind = rng.random()
        if kind < 0.35:
            parts.append("".join(rng.choice(ARABIC + PERSIAN) for _ in range(rng.randint(1, 8))))
        elif kind < 0.6:
            parts.append(rng.choice(LATIN))
        elif kind < 0.75:
            parts.append("".join(rng.choice(DIGITS) for _ in range(rng.randint(1, 4))))
        elif kind < 0.9:
            parts.append(rng.choice(NEUTRALS))
        else:
            parts.append(rng.choice(CONTROLS))
    return "".join(parts)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--cases", type=Path)
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--limit", type=int, default=400)
    parser.add_argument("--quiet", action="store_true")
    args = parser.parse_args()

    lines = []
    if args.cases and args.cases.exists():
        lines += [x for x in args.cases.read_text(encoding="utf-8").splitlines() if x]
    rng = random.Random(args.seed)
    lines += [random_line(rng) for _ in range(args.limit)]
    # python-bidi and the probe both treat a line as one paragraph, so no embedded newlines.
    lines = [x.replace("\n", " ").replace("\r", " ") for x in lines]

    probe = subprocess.run(
        [str(PROBE)], input="\n".join(lines), capture_output=True, text=True,
        encoding="utf-8", errors="replace",
    )
    if probe.returncode != 0:
        print(probe.stderr, file=sys.stderr)
        return 2
    got = probe.stdout.splitlines()
    if len(got) != len(lines):
        print(f"probe returned {len(got)} lines for {len(lines)} inputs", file=sys.stderr)
        return 2

    failures = 0
    for text, out in zip(lines, got):
        levels, _, visual = out.partition("|")
        expected = bidi_ref.get_display(text)
        if visual != expected:
            failures += 1
            if failures <= 20:
                print(f"MISMATCH  input={text!r}\n    ours    ={visual!r}\n    ref     ={expected!r}\n    levels  ={levels}")
    if not args.quiet:
        print(f"\n{len(lines) - failures}/{len(lines)} cases agree with python-bidi")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
