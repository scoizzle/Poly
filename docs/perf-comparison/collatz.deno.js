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

const limit = parseInt(Deno.args[0]) || 10000;
collatz(limit); // warmup
const start = performance.now();
const [bestN, maxLen] = collatz(limit);
const us = (performance.now() - start) * 1000;
console.log(`Deno,${limit},${bestN}:${maxLen},${us.toFixed(0)}`);
