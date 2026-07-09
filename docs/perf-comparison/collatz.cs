using System.Diagnostics;

int limit = args.Length > 0 ? int.Parse(args[0]) : 10000;

(int bestN, int maxLen) Collatz(int limit) {
    int bestN = 0, maxLen = 0;
    for (int n = 1; n <= limit; n++) {
        long m = n;
        int len = 0;
        while (m != 1) {
            if (m % 2 == 0) m /= 2;
            else m = m * 3 + 1;
            len++;
        }
        if (len > maxLen) {
            maxLen = len;
            bestN = n;
        }
    }
    return (bestN, maxLen);
}

var sw = Stopwatch.StartNew();
var (bestN, maxLen) = Collatz(limit);
sw.Stop();
Console.WriteLine($"C# native,{limit},{bestN}:{maxLen},{sw.Elapsed.TotalMicroseconds:F0}");
