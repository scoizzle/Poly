#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <time.h>

void collatz(int limit, int* best_n, int* max_len) {
    *max_len = 0;
    *best_n = 0;
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
        if (len > *max_len) {
            *max_len = len;
            *best_n = n;
        }
    }
}

int main(int argc, char** argv) {
    int limit = argc > 1 ? atoi(argv[1]) : 10000;
    int best_n, max_len;
    collatz(limit, &best_n, &max_len); /* warmup */
    struct timespec start, end;
    clock_gettime(CLOCK_MONOTONIC, &start);
    collatz(limit, &best_n, &max_len);
    clock_gettime(CLOCK_MONOTONIC, &end);
    double us = (end.tv_sec - start.tv_sec) * 1000000.0
              + (end.tv_nsec - start.tv_nsec) / 1000.0;
    printf("C,%d,%d:%d,%.0f\n", limit, best_n, max_len, us);
    return 0;
}
