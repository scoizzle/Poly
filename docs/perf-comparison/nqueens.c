#include <stdio.h>
#include <stdlib.h>
#include <time.h>

int nqueens(void) {
    int total = 0;
    int all = 255;
    int cols[8], ld[8], rd[8], tried[8];
    int row = 0;
    cols[0] = 0; ld[0] = 0; rd[0] = 0; tried[0] = 0;

    while (row >= 0) {
        int avail = all & ~(cols[row] | ld[row] | rd[row]) & ~tried[row];
        if (avail == 0) {
            tried[row] = 0;
            row--;
            continue;
        }
        int bit = avail & -avail;
        tried[row] |= bit;
        if (row == 7) {
            total++;
        } else {
            row++;
            cols[row] = cols[row - 1] | bit;
            ld[row] = (ld[row - 1] | bit) << 1;
            rd[row] = (rd[row - 1] | bit) >> 1;
            tried[row] = 0;
        }
    }
    return total;
}

int main(int argc, char** argv) {
    (void)argc; (void)argv;
    struct timespec start, end;
    clock_gettime(CLOCK_MONOTONIC, &start);
    int result = nqueens();
    clock_gettime(CLOCK_MONOTONIC, &end);
    double ms = (end.tv_sec - start.tv_sec) * 1000.0
              + (end.tv_nsec - start.tv_nsec) / 1000000.0;
    printf("C,8,%d,%.1f\n", result, ms);
    return 0;
}
