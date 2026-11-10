#!/usr/bin/env bash
# PreToolUse (Bash): bloqueia comandos destrutivos ou que pulam o fluxo de PR.
# Lê o JSON do hook em stdin; exit 2 bloqueia e devolve a mensagem ao agente.
input="$(cat)"
cmd="$(printf '%s' "$input" | python3 -c 'import sys,json;print(json.load(sys.stdin).get("tool_input",{}).get("command",""))' 2>/dev/null || printf '%s' "$input")"

block() { echo "BLOQUEADO pelo hook da software house: $1" >&2; exit 2; }

echo "$cmd" | grep -Eq 'git[[:space:]]+push([^|;&]*)[[:space:]](--force|-f|--force-with-lease)([[:space:]]|$)' && block "force push é proibido."
echo "$cmd" | grep -Eq 'git[[:space:]]+push([^|;&]*)[[:space:]](origin[[:space:]]+)?(HEAD:)?(main|master)([[:space:]]|$)' && block "push direto em main é proibido; abra um PR."
echo "$cmd" | grep -Eq 'gh[[:space:]]+pr[[:space:]]+merge' && block "merge é decisão humana."
echo "$cmd" | grep -Eq 'gh[[:space:]]+(repo[[:space:]]+delete|api[^|;&]*-X[[:space:]]*DELETE)' && block "operação destrutiva no GitHub."
echo "$cmd" | grep -Eq 'rm[[:space:]]+-[a-zA-Z]*r[a-zA-Z]*f?[[:space:]]+(/|~|\$HOME|\.git)([[:space:]/]|$)' && block "rm -rf em diretório crítico."
echo "$cmd" | grep -Eq 'git[[:space:]]+reset[[:space:]]+--hard[[:space:]]+origin' && block "reset --hard em origin descarta trabalho; peça ao humano."
echo "$cmd" | grep -Eq '(terraform|tofu)[[:space:]]+(apply|destroy|import|state[[:space:]]+(rm|mv|push))' && block "apply/destroy/state só pelo CI com aprovação humana."
echo "$cmd" | grep -Eq '(firebase[[:space:]]+deploy|fly(ctl)?[[:space:]]+(deploy|apps[[:space:]]+destroy)|railway[[:space:]]+(up|down|redeploy)|eas[[:space:]]+(submit|update)|kubectl[[:space:]]+(apply|delete|rollout)|helm[[:space:]]+(install|upgrade|uninstall)|gcloud[[:space:]]+[a-z-]+([[:space:]]+[a-z-]+)?[[:space:]]+deploy|az[[:space:]]+[a-z-]+[[:space:]]+[a-z-]*[[:space:]]*deploy|aws[[:space:]]+cloudformation[[:space:]]+deploy|vercel[[:space:]]+(deploy|--prod))' && block "deploy é feito pelo CI depois do merge humano (staging) e por disparo manual humano (produção)."
echo "$cmd" | grep -Eqi 'gh[[:space:]]+run[[:space:]]+rerun|gh[[:space:]]+workflow[[:space:]]+run[^|;&]*(deploy|release|prod|rollback)|gh[[:space:]]+api[^|;&]*/dispatches|gh[[:space:]]+release[[:space:]]+(create|edit|delete)' && block "disparar deploy/release ou reexecutar runs é ação humana (falha instável: use o subagente debugger)."
[ -z "${GITHUB_ACTIONS:-}" ] && echo "$cmd" | grep -Eq 'add-label[^|;&]*review:approved' && block "a label review:approved só é aplicada pela revisão automática; o autor pede nova revisão com re-review."
echo "$cmd" | grep -Eq 'size:exception' && block "a label size:exception só pode ser aplicada por um humano; divida o PR."
exit 0
