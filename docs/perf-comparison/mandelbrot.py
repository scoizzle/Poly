import time

def mandelbrot():
    total = 0
    for y in range(128):
        cy = (y - 64) * 8
        for x in range(128):
            cx = (x - 64) * 8
            zx = 0
            zy = 0
            iter = 0
            while iter < 256:
                zx2 = (zx * zx) >> 8
                zy2 = (zy * zy) >> 8
                if (zx2 + zy2) > 1024:
                    break
                zy = ((zx * zy) >> 7) + cy
                zx = zx2 - zy2 + cx
                iter += 1
            total += iter
    return total

if __name__ == '__main__':
    start = time.perf_counter()
    result = mandelbrot()
    ms = (time.perf_counter() - start) * 1000
    print(f"Python,128,{result},{ms:.1f}")
