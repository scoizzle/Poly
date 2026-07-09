import time
import numpy as np

def mandelbrot(size=128, max_iter=256):
    x = np.arange(size, dtype=np.int64) - 64
    y = np.arange(size, dtype=np.int64) - 64
    cx, cy = np.meshgrid(x * 8, y * 8)
    zx = np.zeros((size, size), dtype=np.int64)
    zy = np.zeros((size, size), dtype=np.int64)
    total = 0
    for _ in range(max_iter):
        zx2 = (zx * zx) >> 8
        zy2 = (zy * zy) >> 8
        mask = (zx2 + zy2) <= 1024
        total += int(np.sum(mask))
        if not np.any(mask):
            break
        zx_new = zx2 - zy2 + cx
        zy_new = ((zx * zy) >> 7) + cy
        zx = np.where(mask, zx_new, zx)
        zy = np.where(mask, zy_new, zy)
    return total

if __name__ == '__main__':
    start = time.perf_counter()
    result = mandelbrot()
    us = (time.perf_counter() - start) * 1_000_000
    print(f"Python+NumPy,128,{result},{us:.0f}")
