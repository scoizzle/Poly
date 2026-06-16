using System.Diagnostics;

int NQueens() {
    int total = 0, all = 255;
    int[] cols = new int[8], ld = new int[8], rd = new int[8], tried = new int[8];
    int row = 0;
    cols[0] = ld[0] = rd[0] = tried[0] = 0;

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

var sw = Stopwatch.StartNew();
int result = NQueens();
sw.Stop();
Console.WriteLine($"C# native,8,{result},{sw.ElapsedMilliseconds}");
