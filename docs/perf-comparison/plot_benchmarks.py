import csv, os, sys
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RESULTS_DIR = os.path.join(ROOT, "docs/perf-comparison/results")

# ── Palette & labels ──
POLY_COLOR = "#e66101"
POLY_PREP = "#fdb863"
NATIVE_COLOR = "#5e3c99"
OTHER_COLORS = [
    "#b2b2b2", "#888888", "#666666", "#a6cee3",
    "#1f78b4", "#b2df8a", "#33a02c", "#fb9a99",
    "#e31a1c", "#fdbf6f", "#ff7f00", "#cab2d6",
]

TITLES = {
    "sieve":  "Sieve of Eratosthenes (1M limit) — 78,498 primes",
    "mandelbrot": "Mandelbrot (128 iterations) — 458,080 escapes",
    "nqueens": "N-Queens (size 8) — 92 solutions",
    "collatz": "Collatz (1M limit) — 837,799:524, 524 steps",
}


def plot_csv(csv_path):
    base = os.path.splitext(csv_path)[0]
    out_png = base + ".png"

    if (os.path.exists(out_png)):
        print(f"Skipping {out_png[RESULTS_DIR.__len__() + 1:]}, already exists.")
        return

    data = {}
    with open(csv_path) as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get("result") == "FAILED":
                continue
            data.setdefault(row["benchmark"], []).append(row)

    for bm in data:
        data[bm].sort(key=lambda r: float(r["time_ms"]))

    fig, axes = plt.subplots(2, 2, figsize=(16, 10))
    fig.suptitle("Poly VM Benchmark Comparison — Execution Time (ms, log scale, lower is better)",
                 fontsize=16, fontweight="bold", y=1.02)

    for ax, (bm_name, rows) in zip(axes.flat, data.items()):
        labels = []
        exec_times = []
        prep_times = []
        bar_colors = []

        for r in rows:
            lang = r["language"]
            exec_ms = float(r["time_ms"])
            prep_ms = float(r.get("prep_ms", "0") or "0")

            labels.append(lang)
            exec_times.append(max(exec_ms, 0.001))
            prep_times.append(prep_ms if prep_ms > 0 else 0)

            if "Poly" in lang:
                bar_colors.append(POLY_COLOR)
            elif "native" in lang or "vectorized" in lang:
                bar_colors.append(NATIVE_COLOR)
            else:
                idx = len(bar_colors) % len(OTHER_COLORS)
                bar_colors.append(OTHER_COLORS[idx])

        y_pos = np.arange(len(labels))

        bars_exec = ax.barh(y_pos, exec_times, height=0.6,
                            color=bar_colors, zorder=3)

        for i, (r, prep) in enumerate(zip(rows, prep_times)):
            if prep > 0:
                exec_ms = exec_times[i]
                ax.barh(i, prep, left=exec_ms, height=0.6,
                        color=POLY_PREP, zorder=3)
                ax.text(exec_ms + prep, i, f"  {int(prep)}ms prep",
                        va="center", fontsize=7, color="#666")

        for i, (bar, val) in enumerate(zip(bars_exec, exec_times)):
            display = f"{val:.1f}" if val >= 1 else f"{val:.3f}"
            ax.text(val, i, f"  {display}",
                    va="center", fontsize=7,
                    color="white" if bar_colors[i] in (POLY_COLOR, NATIVE_COLOR) else "#333",
                    fontweight="bold" if bar_colors[i] == POLY_COLOR else "normal")

        ax.set_yticks(y_pos)
        ax.set_yticklabels(labels, fontsize=9)
        ax.set_xscale("log")
        ax.set_xlabel("ms (log scale)", fontsize=9)
        ax.set_title(TITLES.get(bm_name, bm_name), fontsize=11, fontweight="bold")
        ax.invert_yaxis()
        ax.axvline(x=1, color="#ccc", linewidth=0.5, zorder=0)
        ax.grid(axis="x", alpha=0.3, zorder=0)

        for i, r in enumerate(rows):
            if "Poly" in r["language"]:
                ax.annotate("", xy=(0.001, i - 0.3), xytext=(0.001, i + 0.3),
                            arrowprops=dict(arrowstyle="<->", color=POLY_COLOR, lw=2))
                break

    legend_elements = [
        mpatches.Patch(color=POLY_COLOR, label="Poly VM (exec)"),
        mpatches.Patch(color=POLY_PREP, label="Poly VM (compilation prep)"),
        mpatches.Patch(color=NATIVE_COLOR, label="C# native / vectorized"),
        mpatches.Patch(color="#b2b2b2", label="Other languages"),
    ]
    fig.legend(handles=legend_elements, loc="lower center",
               ncol=4, fontsize=10, frameon=True)

    plt.tight_layout()
    plt.savefig(out_png, dpi=150, bbox_inches="tight")
    plt.close()
    print(f"  → {os.path.basename(out_png)}")


# ── Main ──
csv_files = sorted(f for f in os.listdir(RESULTS_DIR) if f.endswith(".csv"))
if not csv_files:
    print("No CSV files found in", RESULTS_DIR)
    sys.exit(1)

for fname in csv_files:
    path = os.path.join(RESULTS_DIR, fname)
    print(f"Plotting: {fname}")
    plot_csv(path)
