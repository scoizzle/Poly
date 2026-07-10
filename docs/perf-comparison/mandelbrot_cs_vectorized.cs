using System.Diagnostics;

long Mandelbrot() {
    long total = 0;
    for (int y = 0; y < 128; y++) {
        long cy = (y - 64L) * 8;
        for (int x = 0; x < 128; x++) {
            long cx = (x - 64L) * 8;
            long zx = 0, zy = 0;
            int iter = 0;
            while (iter < 256) {
                long zx2 = (zx * zx) >> 8;
                long zy2 = (zy * zy) >> 8;
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

_ = Mandelbrot(); // warmup
var sw = Stopwatch.StartNew();
long result = Mandelbrot();
sw.Stop();
Console.WriteLine($"C# vectorized,128,{result},{sw.Elapsed.TotalMicroseconds:F0}");
