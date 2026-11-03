"""Differential check against Ludwig V.'s PSA reference layout and arithmetic.

Reference: https://github.com/ludwig-v/psa-seedkey-algorithm/blob/main/algorithm.py
Original algorithm: Wouter Bokslag & Jason F.; GPL-3.0-or-later, as is the
firmware. License: ../../modulo_c4_hibrido/COPYING. No vehicle access occurs.
"""
import random
import subprocess
import sys
from pathlib import Path


def reference_transform(value, secret):
    value &= 0xFFFF
    if value & 0x8000:
        value -= 0x10000
    sec0, sec1, sec2 = secret
    quotient = int(value / sec0)  # truncate toward zero, as in C/AVR
    remainder = value - quotient * sec0
    result = remainder * sec2 - quotient * sec1
    if result < 0:
        result += sec0 * sec2 + sec1
    return result & 0xFFFF


def reference_key(pin, seed):
    b0, b1, b2, b3 = seed.to_bytes(4, "big")
    first, second = (0xB2, 0x3F, 0xAA), (0xB1, 0x02, 0xAB)
    high = reference_transform(pin, first) | reference_transform((b0 << 8) | b3, second)
    low = reference_transform((b1 << 8) | b2, first) | reference_transform(high, second)
    return (high << 16) | low


rng = random.Random(0xD91C)
cases = [(pin, 0x12345678) for pin in range(65536)]
cases += [(0xD91C, (word << 16) | (word ^ 0xFFFF)) for word in range(65536)]
cases += [(rng.randrange(65536), rng.getrandbits(32)) for _ in range(4096)]
edges = [0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF, 0x11111111, 0xAABBCCDD]
cases += [(0xD91C, seed) for seed in edges]
wire = "".join(f"{pin:04X} {seed:08X}\n" for pin, seed in cases)
executable = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).with_name("seed_vectors.exe")
result = subprocess.run([str(executable)],
                        input=wire, text=True, capture_output=True, check=True)
keys = result.stdout.splitlines()
assert len(keys) == len(cases)
for (pin, seed), actual in zip(cases, keys):
    expected = reference_key(pin, seed)
    assert int(actual, 16) == expected, (hex(pin), hex(seed), actual, hex(expected))
print(f"PASS: {len(cases)} independent seed/key vectors, including all 16-bit PINs, signed boundaries and randomized seeds")
print(f"PIN D91C seed 12345678 -> {reference_key(0xD91C, 0x12345678):08X}")
