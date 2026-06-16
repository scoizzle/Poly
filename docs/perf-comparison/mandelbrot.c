#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <time.h>

int64_t mandelbrot(void) {
    int64_t total = 0;
    for (int y = 0; y < 128; y++) {
        int64_t cy = (int64_t)(y - 64) * 8;
        for (int x = 0; x < 128; x++) {
            int64_t cx = (int64_t)(x - 64) * 8;
            int64_t zx = 0, zy = 0;
            int iter = 0;
            while (iter < 256) {
                int64_t zx2 = (zx * zx) >> 8;
                int64_t zy2 = (zy * zy) >> 8;
                if ((zx2 + zy2) > 1024) break;
                zy = ((zx * zy) >> 7) + cy;
                zx = zx2 - zy2 + cx;
                iter++;
            }
            total += iter;
        }
    }
    return total;
}

int main(int argc, char** argv) {
    (void)argc; (void)argv;
    struct timespec start, end;
    clock_gettime(CLOCK_MONOTONIC, &start);
    int64_t result = mandelbrot();
    clock_gettime(CLOCK_MONOTONIC, &end);
    double ms = (end.tv_sec - start.tv_sec) * 1000.0
              + (end.tv_nsec - start.tv_nsec) / 1000000.0;
    printf("C,128,%lld,%.1f\n", (long long)result, ms);
    return 0;
}
