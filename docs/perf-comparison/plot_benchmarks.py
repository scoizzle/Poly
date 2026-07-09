import csv, os, sys
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RESULTS_DIR = os.path.join(ROOT, "docs/perf-comparison/results")

# ── Tier-based palette ──
# The story: Poly VM sits with compiled languages, not interpreted ones.
POLY_HERO   = "#e66101"  # bright orange — the hero, the thing we're evaluating
POLY_NORMAL = "#fdb863"  # lighter orange for Normal (debug) mode
PREP_TINT   = "#fdd0a2"  # faint orange for prep markers
NATIVE_BLUE = "#3182bd"  # C# native/vectorized — the reference baseline
COMPILED_TEAL = "#74a9cf"  # C/C++/Rust — same tier as Poly VM
INTERP_GRAY = "#cccccc"  # everything interpreted — background noise

TITLES = {
    "sieve":  "Sieve of Eratosthenes (1M limit) — 78,498 primes",
    "mandelbrot": "Mandelbrot (128 iterations) — 458,080 escapes",
    "nqueens": "N-Queens (size 8) — 92 solutions",
    "collatz": "Collatz (1M limit) — 837,799:524, 524 steps",
}


def _tier_color(lang):
    """Assign a color based on what tier the language belongs to."""
    if "Poly" in lang:
        if "double" in lang or "raw" in lang:
            return INTERP_GRAY  # curiosities, not the story
        return POLY_HERO if "Normal" not in lang else POLY_NORMAL
    if lang in ("C", "C++", "Rust"):
        return COMPILED_TEAL
    if "native" in lang or "vectorized" in lang:
        return NATIVE_BLUE
    return INTERP_GRAY


def _is_poly(lang):
    return "Poly" in lang and "double" not in lang


def plot_csv(csv_path):
    base = os.path.splitext(csv_path)[0]
    out_png = base + ".png"

    if os.path.exists(out_png):
        print(f"Skipping {os.path.basename(out_png)}, already exists.")
        return

    data = {}
    with open(csv_path) as f:
        reader = csv.DictReader(f)
        for row in reader:
            if row.get("result") == "FAILED":
                continue
            data.setdefault(row["benchmark"], []).append(row)

    for bm in data:
        for key in ("avg_us", "avg_ms", "time_ms"):
            if key in data[bm][0]:
                time_key = key
                break
        else:
            continue
        if time_key in ("time_ms", "avg_ms"):
            for r in data[bm]:
                val = r.get(time_key)
                if val is None or val == "":
                    continue
                r["avg_us"] = str(float(val) * 1000.0)
                min_ms = r.get("min_ms")
                max_ms = r.get("max_ms")
                if min_ms is not None and min_ms != "":
                    r["min_us"] = str(float(min_ms) * 1000.0)
                if max_ms is not None and max_ms != "":
                    r["max_us"] = str(float(max_ms) * 1000.0)
            time_key = "avg_us"
        data[bm] = [r for r in data[bm] if r.get(time_key)]
        if not data[bm]:
            continue
        data[bm].sort(key=lambda r: float(r[time_key]))

    fig, axes = plt.subplots(2, 2, figsize=(16, 10))
    fig.suptitle("Poly VM Sits With Compiled Languages — Not the Bottleneck",
                 fontsize=16, fontweight="bold", y=1.02)

    for ax, (bm_name, rows) in zip(axes.flat, data.items()):
        labels = []
        avg_times = []
        min_times = []
        max_times = []
        colors = []

        # Find C# native baseline for annotation
        cs_native_avg = None
        for r in rows:
            if "native" in r["language"] and "vectorized" not in r["language"]:
                cs_native_avg = float(r["avg_us"])

        for r in rows:
            lang = r["language"]
            avg_val = float(r["avg_us"])
            min_val = float(r.get("min_us", 0) or 0) or avg_val
            max_val = float(r.get("max_us", 0) or 0) or avg_val

            labels.append(lang)
            avg_times.append(max(avg_val, 0.001))
            min_times.append(min_val if min_val > 0 else avg_val * 0.9)
            max_times.append(max_val)
            colors.append(_tier_color(lang))

        y_pos = np.arange(len(labels))

        # ── Plot: one clean bar per language ──
        for i in range(len(avg_times)):
            c = colors[i]
            # Subtle error bar (min–max)
            ax.plot([min_times[i], max_times[i]], [i, i],
                    color=c, linewidth=0.8, zorder=2)
            ax.scatter([min_times[i], max_times[i]], [i, i],
                       s=6, color=c, edgecolors="none", zorder=3)
            # Main avg bar
            ax.barh(i, avg_times[i], height=0.55, color=c,
                    edgecolor="none", zorder=4)

        # ── Reference line at C# native ──
        if cs_native_avg:
            ax.axvline(x=cs_native_avg, color=NATIVE_BLUE, linewidth=0.8,
                       linestyle="--", alpha=0.5, zorder=1)
            ax.text(cs_native_avg, -0.4, "C# native",
                    ha="center", va="bottom", fontsize=6,
                    color=NATIVE_BLUE, alpha=0.6, style="italic")

        # ── Poly VM ratio annotation ──
        for r in rows:
            if _is_poly(r["language"]) and "Normal" not in r["language"]:
                poly_avg = float(r["avg_us"])
                if cs_native_avg:
                    ratio = poly_avg / cs_native_avg
                    ax.text(0.98, 0.02,
                            f"Poly VM ≈ {ratio:.1f}× C# native",
                            transform=ax.transAxes, ha="right", va="bottom",
                            fontsize=9, color=POLY_HERO, fontweight="bold",
                            bbox=dict(boxstyle="round,pad=0.3",
                                      facecolor="white", edgecolor=POLY_HERO,
                                      alpha=0.85))
                break

        ax.set_yticks(y_pos)
        ax.set_yticklabels(labels, fontsize=9)
        ax.set_xscale("log")
        ax.set_xlabel("execution time (µs, log scale)", fontsize=8)
        ax.set_title(TITLES.get(bm_name, bm_name), fontsize=11, fontweight="bold")
        ax.invert_yaxis()
        ax.tick_params(axis="x", labelsize=7)
        ax.grid(axis="x", alpha=0.2, zorder=0)

    # ── Legend: tiered, clean ──
    legend_elements = [
        mpatches.Patch(color=POLY_HERO,   label="Poly VM (NoDebug)"),
        mpatches.Patch(color=POLY_NORMAL, label="Poly VM (Normal / debug)"),
        mpatches.Patch(color=NATIVE_BLUE, label="C# native / vectorized"),
        mpatches.Patch(color=COMPILED_TEAL, label="C / C++ / Rust (compiled)"),
        mpatches.Patch(color=INTERP_GRAY, label="Interpreted (Py/JS/Bun/Deno)"),
    ]
    fig.legend(handles=legend_elements, loc="lower center",
               ncol=5, fontsize=9, frameon=True)

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
