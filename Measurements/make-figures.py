#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Zgeneriraj grafe diplomske naloge iz datotek CSV v mapi results/.

Uporaba:
    py make-figures.py                    # vse slike v SVG in PNG
    py make-figures.py --list             # izpis razpoložljivih slik
    py make-figures.py --format png       # samo PNG
    py make-figures.py --format svg       # samo SVG (vektor za Word)
    py make-figures.py --out ../../diplomaObrazec/slike

Slike so črno-bele, s serifno pisavo in decimalno vejico, da se ujemajo
z besedilom naloge. Vsaka slika se izrise iz datoteke CSV, zato jo je po
ponovni meritvi dovolj znova zgenerirati.
"""

import argparse
import csv
import os
import sys

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter, MaxNLocator

HERE = os.path.dirname(os.path.abspath(__file__))
RESULTS = os.path.normpath(os.path.join(HERE, "..", "results"))
DEFAULT_OUT = os.path.normpath(os.path.join(HERE, "..", "..", "diplomaObrazec", "slike"))

# --------------------------------------------------------------------- slog

FIGSIZE = (10, 5.33)          # pri dpi 270 da 2700 x 1440 px
PNG_DPI = 270

#: Crno-bel slog: temno polno, svetlo s crtami, svetlo polno, belo s krizci.
STYLES = [
    {"color": "#404040", "hatch": None},
    {"color": "#D9D9D9", "hatch": "///"},
    {"color": "#BFBFBF", "hatch": None},
    {"color": "#FFFFFF", "hatch": "xxx"},
]

#: Slovenska imena izvedb; kljuc je vrednost iz stolpca Architecture.
ARCH_LABELS = {
    "Monolith": "Plastni monolit",
    "Monolith (Layered)": "Plastni monolit",
    "Monolith (fail-fast)": "Plastni monolit (fail-fast)",
    "Hexagonal": "Hexagonalna",
    "Hexagonal (Ports & Adapters)": "Hexagonalna",
    "Messaging": "Sporočilna",
    "Messaging (Channel-based)": "Sporočilna",
    "Shared (common)": "Skupni modul",
}


def setup_style():
    plt.rcParams.update({
        "font.family": "serif",
        "font.serif": ["Times New Roman", "DejaVu Serif", "Georgia"],
        "font.size": 15,
        "axes.titlesize": 15,
        "axes.labelsize": 15,
        "xtick.labelsize": 15,
        "ytick.labelsize": 15,
        "legend.fontsize": 15,
        "axes.edgecolor": "#000000",
        "axes.linewidth": 1.0,
        "figure.facecolor": "white",
        "savefig.facecolor": "white",
        "hatch.linewidth": 1.0,
        "svg.fonttype": "none",   # besedilo v SVG ostane besedilo
    })


def si(value, decimals=3):
    """Stevilka z decimalno vejico, kot se pise v nalogi."""
    return ("%.*f" % (decimals, value)).replace(".", ",")


def comma_axis(decimals=2):
    return FuncFormatter(lambda v, _pos: si(v, decimals))


def label(arch):
    return ARCH_LABELS.get(arch, arch)


def zapisi(n):
    """Pravilna slovenska oblika za stevilo zapisov (ednina, dvojina, mnozina)."""
    n = n % 100
    if n == 1:
        return "zapis"
    if n == 2:
        return "zapisa"
    if n in (3, 4):
        return "zapisi"
    return "zapisov"


def read_csv(name):
    path = os.path.join(RESULTS, name)
    if not os.path.isfile(path):
        raise SystemExit("Manjka datoteka: %s" % path)
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def finish(ax, ylabel, legend_labels=None, ncol=4, decimals=2, ymax_pad=1.18):
    """Skupno pospravljanje osi: mreza, robovi, legenda, decimalna vejica."""
    ax.set_ylabel(ylabel)
    # Okrogli koraki na osi y (0,05 namesto 0,025), da se oznake ne zaokrozujejo
    # v vrednosti, ki ne ustrezajo dejanskemu polozaju crte.
    ax.yaxis.set_major_locator(MaxNLocator(nbins=6, steps=[1, 2, 2.5, 5, 10]))
    ax.yaxis.set_major_formatter(comma_axis(decimals))
    ax.yaxis.grid(True, color="#D0D0D0", linewidth=0.8)
    ax.xaxis.grid(False)
    ax.set_axisbelow(True)
    for side in ("top", "right"):
        ax.spines[side].set_visible(False)
    ax.tick_params(axis="both", length=0)
    top = ax.get_ylim()[1]
    ax.set_ylim(0, top * ymax_pad if top else 1)
    if legend_labels:
        ax.legend(legend_labels, loc="upper center", bbox_to_anchor=(0.5, -0.10),
                  ncol=ncol, frameon=False, handlelength=1.6, handleheight=1.1,
                  columnspacing=2.4)


def grouped_bars(ax, groups, series, decimals=3, value_fmt=None):
    """groups: [ime skupine]; series: [(ime serije, [vrednosti])]."""
    n = len(series)
    width = 0.8 / n
    xs = range(len(groups))
    bars = []
    # Pri stirih serijah so stolpci ozji, zato so oznake vrednosti manjse,
    # sicer sezejo cez sosednji stolpec.
    label_size = 13 if n <= 3 else 11
    for i, (_name, values) in enumerate(series):
        st = STYLES[i % len(STYLES)]
        offs = [x - 0.4 + width * (i + 0.5) for x in xs]
        b = ax.bar(offs, values, width * 0.92, color=st["color"], hatch=st["hatch"],
                   edgecolor="black", linewidth=0.8, zorder=3)
        bars.append(b)
        for rect, v in zip(b, values):
            txt = value_fmt(v) if value_fmt else si(v, decimals)
            ax.annotate(txt, (rect.get_x() + rect.get_width() / 2, rect.get_height()),
                        xytext=(0, 4), textcoords="offset points",
                        ha="center", va="bottom", fontsize=label_size)
    ax.set_xticks(list(xs))
    ax.set_xticklabels(groups)
    return bars


# ------------------------------------------------------------------- slike

def fig_matrika_velikosti(ax):
    """Slika 6 v nalogi: povprecni cas obdelave po velikosti odgovora."""
    rows = read_csv("performance.csv")
    sizes = []
    for r in rows:
        if r["ResponseSize"] not in sizes:
            sizes.append(r["ResponseSize"])
    archs = []
    for r in rows:
        if r["Architecture"] not in archs:
            archs.append(r["Architecture"])

    def mean_for(arch, size):
        for r in rows:
            if r["Architecture"] == arch and r["ResponseSize"] == size:
                return float(r["MeanMs"])
        return 0.0

    counts = {s: int(next(r["RepoCount"] for r in rows if r["ResponseSize"] == s))
              for s in sizes}
    groups = ["%s (%d %s)" % (s, counts[s], zapisi(counts[s])) for s in sizes]
    series = [(label(a), [mean_for(a, s) for s in sizes]) for a in archs]
    grouped_bars(ax, groups, series, decimals=3)
    finish(ax, "Povprečni čas obdelave (ms)", [s[0] for s in series], ncol=3, decimals=2)
    ax.set_xlabel("Velikost odgovora")


#: ime datoteke -> (funkcija, stevilka slike v nalogi, opis)
FIGURES = {
    "slika5-matrika-velikosti": (fig_matrika_velikosti, 6,
                                 "Povprečni čas obdelave po velikosti odgovora"),
}

#: Slike, ki jih ta skript ne dela (niso graf iz CSV).
NOT_A_CHART = {
    "slika2-obseg-posega": "Slika 5 - odseka kode iz commita e2de80d, ne graf.",
    "slikaA1-plastna-arhitektura": "Slika 1 - shema, narisana v draw.io.",
    "slikaA2-hexagonalna-arhitektura": "Slika 2 - shema, narisana v draw.io.",
    "slikaA3-sporocilna-arhitektura": "Slika 3 - shema, narisana v draw.io.",
    "slikaA4-hexagonalna-izvedba": "Slika 4 - shema, narisana v draw.io.",
}


def render(name, out_dir, formats):
    fn, num, desc = FIGURES[name]
    fig, ax = plt.subplots(figsize=FIGSIZE)
    fn(ax)
    fig.tight_layout()
    written = []
    for ext in formats:
        path = os.path.join(out_dir, name + "." + ext)
        fig.savefig(path, format=ext, dpi=PNG_DPI if ext == "png" else None,
                    bbox_inches="tight", pad_inches=0.15)
        written.append(path)
    plt.close(fig)
    return num, desc, written


def main():
    ap = argparse.ArgumentParser(
        description="Zgeneriraj grafe diplomske naloge iz datotek CSV.")
    ap.add_argument("--out", default=DEFAULT_OUT, help="ciljna mapa")
    ap.add_argument("--format", default="svg,png",
                    help="svg, png ali svg,png (privzeto oboje)")
    ap.add_argument("--only", action="append",
                    help="zgeneriraj samo to sliko (lahko veckrat)")
    ap.add_argument("--list", action="store_true", help="izpisi slike in koncaj")
    args = ap.parse_args()

    if args.list:
        print("Grafi iz CSV:")
        for name, (_fn, num, desc) in sorted(FIGURES.items(), key=lambda kv: kv[1][1]):
            print("  Slika %-3d %-34s %s" % (num, name, desc))
        print("\nNi graf iz CSV:")
        for name, why in sorted(NOT_A_CHART.items()):
            print("  %-34s %s" % (name, why))
        return

    formats = [f.strip().lower() for f in args.format.split(",") if f.strip()]
    for f in formats:
        if f not in ("svg", "png"):
            raise SystemExit("Neznana oblika: %s (uporabi svg ali png)" % f)

    names = args.only or sorted(FIGURES, key=lambda n: FIGURES[n][1])
    for n in names:
        if n not in FIGURES:
            raise SystemExit("Neznana slika: %s (glej --list)" % n)

    os.makedirs(args.out, exist_ok=True)
    setup_style()

    print("Vir podatkov: %s" % RESULTS)
    print("Cilj:         %s\n" % args.out)
    for name in names:
        num, desc, written = render(name, args.out, formats)
        print("Slika %-3d %-34s %s" % (num, name, desc))
        for p in written:
            print("          -> %s (%d KB)" % (os.path.basename(p),
                                               os.path.getsize(p) // 1024))
    print("\nKoncano: %d slik." % len(names))


if __name__ == "__main__":
    main()
