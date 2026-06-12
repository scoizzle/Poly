#include <iostream>
#include <bitset>
#include <memory>
#include <chrono>
#include <string>

static constexpr int64_t MAX_N = 100000000; // 100M bits max

int64_t sieve(int64_t limit) {
    auto bits = std::make_unique<std::bitset<MAX_N>>();
    bits->reset();

    for (int64_t i = 2; i * i <= limit; i++) {
        if (!bits->test(i)) {
            for (int64_t j = i * i; j <= limit; j += i)
                bits->set(j);
        }
    }

    int64_t count = 0;
    for (int64_t i = 2; i <= limit; i++) {
        if (!bits->test(i))
            count++;
    }
    return count;
}

int main(int argc, char** argv) {
    int64_t limit = argc > 1 ? std::atol(argv[1]) : 1000000;
    auto start = std::chrono::steady_clock::now();
    int64_t result = sieve(limit);
    auto end = std::chrono::steady_clock::now();
    double ms = std::chrono::duration<double, std::milli>(end - start).count();
    std::cout << "C++," << limit << "," << result << "," << ms << std::endl;
    return 0;
}
