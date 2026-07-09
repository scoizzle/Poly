import sys
import time

def sieve(limit):
    word_cnt = (limit + 64) // 64
    bits = bytearray(word_cnt * 8)

    i = 2
    while i * i <= limit:
        wi = i >> 6
        bi = i & 63
        word = int.from_bytes(bits[wi*8:(wi+1)*8], 'little')
        if (word >> bi) & 1 == 0:
            j = i * i
            while j <= limit:
                wj = j >> 6
                bj = j & 63
                off = wj * 8
                w = int.from_bytes(bits[off:off+8], 'little')
                w |= 1 << bj
                bits[off:off+8] = w.to_bytes(8, 'little')
                j += i
        i += 1

    count = 0
    for i in range(2, limit + 1):
        wi = i >> 6
        bi = i & 63
        off = wi * 8
        w = int.from_bytes(bits[off:off+8], 'little')
        if (w >> bi) & 1 == 0:
            count += 1
    return count

if __name__ == '__main__':
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else 1000000
    start = time.perf_counter()
    result = sieve(limit)
    us = (time.perf_counter() - start) * 1_000_000
    print(f"Python,{limit},{result},{us:.0f}")
