#include <iostream>
#include <chrono>
#include <cstdint>
#include <string>

void collatz(int limit, int& best_n, int& max_len) {
    max_len = 0;
    best_n = 0;
    for (int n = 1; n <= limit; n++) {
        int64_t m = n;
        int len = 0;
        while (m != 1) {
            if (m % 2 == 0)
                m /= 2;
            else
                m = m * 3 + 1;
            len++;
        }
        if (len > max_len) {
            max_len = len;
            best_n = n;
        }
    }
}

int main(int argc, char** argv) {
    int limit = argc > 1 ? std::atoi(argv[1]) : 10000;
    int best_n, max_len;
    auto start = std::chrono::steady_clock::now();
    collatz(limit, best_n, max_len);
    auto end = std::chrono::steady_clock::now();
    double us = std::chrono::duration<double, std::micro>(end - start).count();
    std::cout << "C++," << limit << "," << best_n << ":" << max_len << "," << us << std::endl;
    return 0;
}
