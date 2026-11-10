#!/usr/bin/env python3
"""Scanner heurístico de "cara de IA" em código de front-end.

Uso: python3 scan.py <pasta ou arquivo> [--json]
Sem dependências. Aponta arquivo:linha para sinais detectáveis por texto.
Heurístico: cada achado é um ponto a olhar, não uma reprovação automática.
"""
import json
import os
import re
import sys
from collections import Counter

EXTS = {".tsx", ".jsx", ".ts", ".js", ".vue", ".svelte", ".astro", ".html", ".css", ".scss", ".mdx", ".dart", ".razor", ".php"}
SKIP_DIRS = {"node_modules", ".next", "dist", "build", ".git", "coverage", ".taste-skill", "__pycache__", ".agents", ".claude"}

# (id, regex, mensagem). Case-insensitive.
RULES = [
    ("instrument-serif", r"instrument[\s_-]?serif", "Instrument Serif: tipografia-assinatura de IA; escolha uma família para o produto"),
    ("italic", r"\bitalic\b|font-style:\s*italic|<em>|<i>", "itálico: confira se é ênfase com critério ou tique"),
    ("negative-kerning", r"letter-spacing:\s*-|tracking-tight(?:er)?\b|tracking-\[-", "kerning negativo: só em display grande (≥48px, ≤ -0.02em)"),
    ("uppercase", r"text-transform:\s*uppercase|\buppercase\b", "ALL CAPS: só em labels ≤12px; nunca em subtítulos"),
    ("gradient-bg", r"bg-gradient-to-|linear-gradient\(|radial-gradient\(|conic-gradient\(|bg-\[radial|mesh", "gradiente de fundo: prefira fundo sólido + acento"),
    ("mono", r"font-mono\b|font-family:\s*[^;]*mono", "mono: só para código, IDs, hashes e dados tabulares"),
    ("divider", r"<hr\b|divide-y|divide-x|border-b\b|border-t\b", "divider: separe por espaçamento, não por linha"),
    ("bordered-box", r"\bborder\b(?![-\w])[^\"'`]*rounded|rounded[^\"'`]*\bborder\b(?![-\w])", "container com borda + raio: caixa dentro de caixa?"),
    ("opacity-text", r"text-(?:white|black|gray|zinc|neutral|slate|stone)[-\w]*/\d{1,2}\b|opacity-[1-7]0\b|rgba\([^)]*,\s*0?\.[1-7]\d*\)", "opacidade em texto: use 3 tons com contraste AA"),
    ("squircle", r"rounded-\[(?:[2-9]|\d{2})(?:rem|px)|rounded-3xl|border-radius:\s*(?:[3-9]\d|\d{3})px", "raio enorme/squircle: raio pela escala do elemento"),
    ("pill", r"rounded-full[^\"'`]*(?:px-|text-xs)|(?:px-|text-xs)[^\"'`]*rounded-full|\bbadge\b|\bpill\b|\bchip\b", "tag/pill/badge: apague as que não mudam uma decisão"),
    ("eyebrow", r"\beyebrow\b|\boverline\b|\bkicker\b|text-xs[^\"'`]*(?:uppercase|tracking-wide)", "eyebrow: rótulo acima do título — remova"),
    ("section-number", r"[\"'>](?:0[1-9]|1[0-2])\s*[—–\-/.]\s*[A-ZÀ-Ú]", "numeração de seção (01 — Features): remova"),
    ("title-period", r"<h[1-3][^>]*>[^<]{4,}\.\s*</h[1-3]>|(?:title|heading|headline)\s*[:=]\s*[\"'][^\"']{4,}\.[\"']", "ponto final em título: remova"),
    ("code-breadcrumb", r"[\"'>]~/[\w./-]+|\$\s*(?:npm|pnpm|yarn|npx)\s+\w+[^\"'`<]*<", "code breadcrumb decorativo: só código real"),
    ("glow-blur", r"blur-(?:2xl|3xl)|backdrop-blur|drop-shadow-\[|shadow-\[0_0_|filter:\s*blur\(", "glow/blur decorativo: embelezamento — remova"),
    ("ascii-dither", r"\bdither|\bascii\b|noise\.(?:png|svg|webp)|grain", "dithering/ASCII/ruído: moda default — substitua pelo produto"),
    ("filler-social-proof", r"trusted by|loved by|used by\s+\d|\d+k\+\s*(?:users|customers|teams|developers)", "prova social vaga: só número/nome real e verificável"),
    ("buzzword-copy", r"seamless(?:ly)?|unlock|supercharge|effortless(?:ly)?|next-gen|game-changing|revolutioniz|empower|elevate your|unleash", "copy genérica: headline específica, afirmação verificável"),
    ("emoji-icon", r"[\U0001F300-\U0001FAFF☀-➿]", "emoji como ícone: use um conjunto de ícones consistente"),
]
COMPILED = [(i, re.compile(rx, re.IGNORECASE), msg) for i, rx, msg in RULES]

POSITIVE = [
    ("tabular-nums", r"tabular-nums|font-variant-numeric"),
    ("text-balance", r"text-balance|text-wrap:\s*balance|text-pretty"),
    ("reduced-motion", r"prefers-reduced-motion|useReducedMotion"),
    ("loading-state", r"skeleton|aria-busy|isLoading|loading=|<Suspense"),
    ("focus-visible", r"focus-visible|focus:ring|:focus"),
]
POS_COMPILED = [(i, re.compile(rx, re.IGNORECASE)) for i, rx in POSITIVE]


def iter_files(root):
    if os.path.isfile(root):
        yield root
        return
    for d, dirs, files in os.walk(root):
        dirs[:] = [x for x in dirs if x not in SKIP_DIRS]
        for f in files:
            if os.path.splitext(f)[1].lower() in EXTS:
                yield os.path.join(d, f)


def scan(root):
    findings, positives, files = [], Counter(), 0
    for path in iter_files(root):
        files += 1
        try:
            with open(path, encoding="utf-8", errors="ignore") as fh:
                for n, line in enumerate(fh, 1):
                    if len(line) > 2000:
                        continue
                    for rid, rx, msg in COMPILED:
                        if rx.search(line):
                            findings.append({"rule": rid, "file": path, "line": n, "msg": msg, "src": line.strip()[:140]})
                    for pid, rx in POS_COMPILED:
                        if rx.search(line):
                            positives[pid] += 1
        except OSError:
            pass
    return findings, positives, files


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    as_json = "--json" in sys.argv
    if not args:
        print(__doc__); sys.exit(2)
    findings, positives, files = scan(args[0])
    if as_json:
        print(json.dumps({"files": files, "findings": findings, "positives": positives}, ensure_ascii=False, indent=1)); return
    by_rule = Counter(f["rule"] for f in findings)
    print(f"# unslop scan — {files} arquivos, {len(findings)} achados\n")
    print("## Sinais de IA (por frequência)")
    for rid, c in by_rule.most_common():
        msg = next(m for i, _, m in RULES if i == rid)
        print(f"- {rid}: {c} — {msg}")
    print("\n## Boas práticas presentes")
    for pid, _ in POSITIVE:
        print(f"- {pid}: {'sim (' + str(positives[pid]) + ')' if positives[pid] else 'NÃO encontrado'}")
    print("\n## Achados (arquivo:linha)")
    for f in findings[:400]:
        print(f"[{f['rule']}] {f['file']}:{f['line']} — {f['src']}")
    if len(findings) > 400:
        print(f"... +{len(findings) - 400} (use --json para tudo)")
    print("\nHeurístico: julgue cada achado na página renderizada. O que o scanner não vê: produto não mostrado, headline vaga, diagramas detalhados demais, texto redundante.")


if __name__ == "__main__":
    main()
