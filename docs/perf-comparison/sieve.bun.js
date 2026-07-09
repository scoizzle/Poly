function sieve(limit) {
    const wordCnt = ((limit + 64) / 64) | 0;
    const bits = new BigUint64Array(wordCnt);
    for (let i = 2; i * i <= limit; i++) {
        const wi = i >> 6;
        const bi = i & 63;
        if (!((bits[wi] >> BigInt(bi)) & 1n)) {
            for (let j = i * i; j <= limit; j += i) {
                const wj = j >> 6;
                const bj = j & 63;
                bits[wj] |= 1n << BigInt(bj);
            }
        }
    }
    let count = 0;
    for (let i = 2; i <= limit; i++) {
        const wi = i >> 6;
        const bi = i & 63;
        if (!((bits[wi] >> BigInt(bi)) & 1n)) count++;
    }
    return count;
}

const limit = parseInt(process.argv[2]) || 1000000;
const start = performance.now();
const result = sieve(limit);
const us = (performance.now() - start) * 1000;
console.log(`Bun,${limit},${result},${us.toFixed(0)}`);
