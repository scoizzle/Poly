function mandelbrot() {
    let total = 0;
    for (let y = 0; y < 128; y++) {
        const cy = (y - 64) * 8;
        for (let x = 0; x < 128; x++) {
            const cx = (x - 64) * 8;
            let zx = 0, zy = 0;
            let iter = 0;
            while (iter < 256) {
                const zx2 = (zx * zx) >> 8;
                const zy2 = (zy * zy) >> 8;
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

const start = performance.now();
const result = mandelbrot();
const us = (performance.now() - start) * 1000;
console.log(`JS,128,${result},${us.toFixed(0)}`);
