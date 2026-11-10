#!/usr/bin/env bash
# Espelha .claude/skills em .agents/skills (Codex), trocando referências a CLAUDE.md por AGENTS.md.
# Portável entre macOS (BSD) e Linux (GNU).
set -euo pipefail
cd "$(dirname "$0")/.."
rm -rf .agents/skills && mkdir -p .agents
cp -R .claude/skills .agents/skills
{ grep -rl 'CLAUDE\.md' .agents/skills || true; } | while IFS= read -r f; do
  sed -i.bak 's/CLAUDE\.md/AGENTS.md/g' "$f" && rm -f "$f.bak"
done
echo "✔ .agents/skills sincronizado"
