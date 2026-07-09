function nqueens() {
    let total = 0;
    const all = 255;
    const cols = new Array(8), ld = new Array(8), rd = new Array(8), tried = new Array(8);
    let row = 0;
    cols[0] = 0; ld[0] = 0; rd[0] = 0; tried[0] = 0;

    while (row >= 0) {
        let avail = all & ~(cols[row] | ld[row] | rd[row]) & ~tried[row];
        if (avail === 0) {
            tried[row] = 0;
            row--;
            continue;
        }
        const bit = avail & -avail;
        tried[row] |= bit;
        if (row === 7) {
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

const start = performance.now();
const result = nqueens();
const us = (performance.now() - start) * 1000;
console.log(`Bun,8,${result},${us.toFixed(0)}`);
