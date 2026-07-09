import sys
import time
import numpy as np

def sieve(limit):
    is_prime = np.ones(limit + 1, dtype=bool)
    is_prime[0:2] = False
    for i in range(2, int(limit ** 0.5) + 1):
        if is_prime[i]:
            is_prime[i * i : limit + 1 : i] = False
    return np.sum(is_prime)

if __name__ == '__main__':
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else 1000000
    start = time.perf_counter()
    result = int(sieve(limit))
    us = (time.perf_counter() - start) * 1_000_000
    print(f"Python+NumPy,{limit},{result},{us:.0f}")
