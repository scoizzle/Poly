using System.Diagnostics;

long Sieve(int limit) {
    int wordCnt = (limit + 64) / 64;
    long[] bits = new long[wordCnt];

    for (int i = 2; i * i <= limit; i++) {
        if ((bits[i >> 6] >> (i & 63) & 1) == 0) {
            for (int j = i * i; j <= limit; j += i)
                bits[j >> 6] |= 1L << (j & 63);
        }
    }

    long count = 0;
    for (int i = 2; i <= limit; i++) {
        if ((bits[i >> 6] >> (i & 63) & 1) == 0)
            count++;
    }
    return count;
}

int limit = args.Length > 0 ? int.Parse(args[0]) : 1000000;
var sw = Stopwatch.StartNew();
long result = Sieve(limit);
sw.Stop();
Console.WriteLine($"C# native,{limit},{result},{sw.ElapsedMilliseconds}");
