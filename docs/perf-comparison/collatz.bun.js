function collatz(limit) {
    let bestN = 0, maxLen = 0;
    for (let n = 1; n <= limit; n++) {
        let m = n;
        let len = 0;
        while (m !== 1) {
            if (m % 2 === 0) m /= 2;
            else m = m * 3 + 1;
            len++;
        }
        if (len > maxLen) {
            maxLen = len;
            bestN = n;
        }
    }
    return [bestN, maxLen];
}

const limit = parseInt(Bun.argv[2]) || 10000;
const start = performance.now();
const [bestN, maxLen] = collatz(limit);
const ms = performance.now() - start;
console.log(`Bun,${limit},${bestN}:${maxLen},${ms.toFixed(1)}`);
