import time

def nqueens():
    total = 0
    all_bits = 255
    cols = [0] * 8
    ld = [0] * 8
    rd = [0] * 8
    tried = [0] * 8
    row = 0
    cols[0] = ld[0] = rd[0] = tried[0] = 0

    while row >= 0:
        avail = all_bits & ~(cols[row] | ld[row] | rd[row]) & ~tried[row]
        if avail == 0:
            tried[row] = 0
            row -= 1
            continue
        bit = avail & -avail
        tried[row] |= bit
        if row == 7:
            total += 1
            continue
        row += 1
        cols[row] = cols[row - 1] | bit
        ld[row] = (ld[row - 1] | bit) << 1
        rd[row] = (rd[row - 1] | bit) >> 1
        tried[row] = 0
    return total

if __name__ == '__main__':
    nqueens()  # warmup
    start = time.perf_counter()
    result = nqueens()
    us = (time.perf_counter() - start) * 1_000_000
    print(f"Python+NumPy,8,{result},{us:.0f}")
