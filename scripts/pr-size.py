#!/usr/bin/env python3
"""Mede o tamanho de um PR (linhas adicionadas + removidas), ignorando arquivos gerados.

Uso:   python3 scripts/pr-size.py [base]          (padrão: origin/main)
Env:   PR_SIZE_SOFT (padrão 400)  aviso acima disso
       PR_SIZE_HARD (padrão 800)  falha acima disso
       PR_LABELS    labels do PR separadas por vírgula (CI); "size:exception" libera o limite
Saída: exit 0 = dentro do limite (ou exceção humana); exit 1 = acima do limite rígido.
"""
import fnmatch
import os
import subprocess
import sys
from pathlib import Path

base = sys.argv[1] if len(sys.argv) > 1 else "origin/main"
soft = int(os.environ.get("PR_SIZE_SOFT", "400"))
hard = int(os.environ.get("PR_SIZE_HARD", "800"))
labels = {l.strip() for l in os.environ.get("PR_LABELS", "").split(",") if l.strip()}
in_ci = os.environ.get("GITHUB_ACTIONS") == "true"

root = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
ignore_file = root / ".github" / "pr-size-ignore"
patterns = []
if ignore_file.exists():
    patterns = [l.strip() for l in ignore_file.read_text().splitlines() if l.strip() and not l.startswith("#")]

TEST_HINTS = ("/test/", "/tests/", "/__tests__/", "/spec/", "Tests/", ".test.", ".spec.", "_test.go", "_test.dart", "/test_", "Test.java", "Test.kt", "Tests.cs")


def ignored(path: str) -> bool:
    for p in patterns:
        if fnmatch.fnmatch(path, p) or (p.startswith("**/") and fnmatch.fnmatch(path, p[3:])):
            return True
    return False


def is_test(path: str) -> bool:
    p = "/" + path
    return any(h in p for h in TEST_HINTS)


out = subprocess.check_output(["git", "diff", "--numstat", f"{base}...HEAD"], text=True)
code = tests = skipped = 0
rows = []
for line in out.splitlines():
    added, removed, path = line.split("\t", 2)
    if " => " in path:  # renomeação: usa o destino
        path = path.split(" => ")[-1].replace("}", "").replace("{", "")
    if added == "-":  # binário
        skipped += 1
        continue
    n = int(added) + int(removed)
    if ignored(path):
        skipped += 1
        continue
    if is_test(path):
        tests += n
    else:
        code += n
    rows.append((n, path))

total = code + tests
print(f"Tamanho do PR: {total} linhas ({code} código + {tests} testes); {skipped} arquivo(s) ignorado(s) (gerados/lock/docs/binários).")
for n, path in sorted(rows, reverse=True)[:8]:
    print(f"  {n:6d}  {path}")

if "size:exception" in labels:
    print("Exceção aprovada por humano (label size:exception).")
    sys.exit(0)

if total > hard:
    msg = f"PR com {total} linhas excede o limite de {hard}. Divida em PRs menores (engineering-standards/references/small-prs.md)."
    print(f"::error::{msg}" if in_ci else f"ERRO: {msg}")
    sys.exit(1)
if total > soft:
    msg = f"PR com {total} linhas passa do ideal de {soft}. Confirme que é um único propósito ou divida."
    print(f"::warning::{msg}" if in_ci else f"AVISO: {msg}")
for lim, name in ((50, "size:xs"), (150, "size:s"), (400, "size:m")):
    if total <= lim:
        print(f"Faixa: {name}")
        break
else:
    print("Faixa: acima de size:m")
