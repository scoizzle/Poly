#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include <time.h>

int64_t sieve(int64_t limit) {
    int64_t word_cnt = (limit + 64) / 64;
    uint64_t* bits = calloc(word_cnt, sizeof(uint64_t));
    if (!bits) return -1;

    for (int64_t i = 2; i * i <= limit; i++) {
        if (!((bits[i >> 6] >> (i & 63)) & 1)) {
            for (int64_t j = i * i; j <= limit; j += i)
                bits[j >> 6] |= (1UL << (j & 63));
        }
    }

    int64_t count = 0;
    for (int64_t i = 2; i <= limit; i++) {
        if (!((bits[i >> 6] >> (i & 63)) & 1))
            count++;
    }
    free(bits);
    return count;
}

int main(int argc, char** argv) {
    int64_t limit = argc > 1 ? atol(argv[1]) : 1000000;
    struct timespec start, end;
    clock_gettime(CLOCK_MONOTONIC, &start);
    int64_t result = sieve(limit);
    clock_gettime(CLOCK_MONOTONIC, &end);
    double ms = (end.tv_sec - start.tv_sec) * 1000.0
              + (end.tv_nsec - start.tv_nsec) / 1000000.0;
    printf("C,%lld,%lld,%.1f\n", (long long)limit, (long long)result, ms);
    return 0;
}
