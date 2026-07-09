#include <iostream>
#include <chrono>
#include <cstdint>

int64_t mandelbrot() {
    int64_t total = 0;
    for (int y = 0; y < 128; y++) {
        int64_t cy = static_cast<int64_t>(y - 64) * 8;
        for (int x = 0; x < 128; x++) {
            int64_t cx = static_cast<int64_t>(x - 64) * 8;
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
    auto start = std::chrono::steady_clock::now();
    int64_t result = mandelbrot();
    auto end = std::chrono::steady_clock::now();
    double us = std::chrono::duration<double, std::micro>(end - start).count();
    std::cout << "C++,128," << result << "," << us << std::endl;
    return 0;
}
