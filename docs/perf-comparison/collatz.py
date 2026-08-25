import sys
import time

def collatz(limit):
    best_n = 0
    max_len = 0
    for n in range(1, limit + 1):
        m = n
        length = 0
        while m != 1:
            if m % 2 == 0:
                m //= 2
            else:
                m = m * 3 + 1
            length += 1
        if length > max_len:
            max_len = length
            best_n = n
    return best_n, max_len

if __name__ == '__main__':
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else 10000
    collatz(limit)  # warmup
    start = time.perf_counter()
    best_n, max_len = collatz(limit)
    us = (time.perf_counter() - start) * 1_000_000
    print(f"Python,{limit},{best_n}:{max_len},{us:.0f}")
