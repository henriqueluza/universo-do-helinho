#!/usr/bin/env python3
"""Checker heurístico de compliance para front-end (código-fonte ou build).

Uso: python3 compliance_check.py <pasta> [--emails <pasta-templates>] [--json]
Sem dependências. Procura documentos legais, consentimento de cookies, scripts de
terceiros antes do consentimento, formulários, alt, dados da empresa, dark patterns
e unsubscribe em e-mails. Heurístico: confirme na página renderizada.
"""
import json
import os
import re
import sys

EXTS = {".tsx", ".jsx", ".ts", ".js", ".vue", ".svelte", ".astro", ".html", ".htm", ".mdx", ".md", ".php", ".razor", ".cshtml", ".dart", ".liquid", ".hbs", ".mjml"}
SKIP = {"node_modules", ".git", ".next", "dist", "build", "coverage", "__pycache__", ".agents", ".claude", ".taste-skill"}

LEGAL = {
    "privacidade": r"privacy|privacidade",
    "termos": r"terms(-|_| )?(of(-|_| )?(service|use))?|termos|tos\b",
    "reembolso": r"refund|reembolso|devolu[cç][aã]o|cancelamento|cancellation",
    "cookies": r"cookie(s)?(-|_| )?polic|pol[ií]tica(-|_| )?de(-|_| )?cookies",
}
THIRD_PARTY = [
    ("Google Analytics / GTM", r"googletagmanager\.com|google-analytics\.com|gtag\(|G-[A-Z0-9]{6,}|UA-\d+-\d"),
    ("Meta Pixel", r"connect\.facebook\.net|fbq\("),
    ("Hotjar / Clarity / FullStory", r"hotjar|clarity\.ms|fullstory"),
    ("TikTok / LinkedIn / Twitter pixel", r"analytics\.tiktok|snap\.licdn|static\.ads-twitter"),
    ("Intercom / Crisp / Drift / HubSpot chat", r"intercom|crisp\.chat|drift\.com|js\.hs-scripts"),
    ("Google Fonts (vaza IP)", r"fonts\.googleapis\.com|fonts\.gstatic\.com"),
    ("Google Maps", r"maps\.googleapis\.com"),
    ("Sentry / Datadog / LogRocket", r"sentry\.io|datadoghq|logrocket"),
    ("Stripe / PayPal / Mercado Pago", r"js\.stripe\.com|paypal\.com/sdk|sdk\.mercadopago"),
    ("YouTube / Vimeo embed", r"youtube\.com/embed|player\.vimeo"),
    ("Segment / Mixpanel / Amplitude / PostHog", r"segment\.com|mixpanel|amplitude|posthog"),
]
CONSENT = r"cookie(-|_)?(consent|banner|notice)|consent(-|_)?(manager|mode)|CookieConsent|onetrust|cookiebot|iubenda|termly|usercentrics|klaro|tarteaucitron|osano"
DARK = [
    ("confirmshaming", r"n[aã]o,?\s+(eu\s+)?(prefiro|quero)\s+(pagar|perder|continuar)|no,?\s+i\s+(don'?t\s+want|prefer\s+to\s+pay|like\s+paying)"),
    ("urgência/escassez", r"s[oó]\s+hoje|[uú]ltimas?\s+\d+\s+(vagas|unidades)|only\s+\d+\s+left|\d+\s+(pessoas|people)\s+(vendo|viewing)|oferta\s+expira|hurry|act\s+now"),
    ("pré-seleção", r"defaultChecked|checked(=|:)\s*(true|\{true\}|\"checked\"|'checked')|\bchecked\b(?=[^>]*type=[\"']checkbox)"),
    ("renovação automática sem destaque", r"auto[- ]?renew|renova[cç][aã]o\s+autom[aá]tica"),
    ("prova social vaga", r"trusted by|loved by|used by\s+\d|\d+k\+\s*(users|customers|clientes|empresas)"),
    ("alegação absoluta", r"\b(#1|n[uú]mero\s+1|o\s+melhor|the\s+best|o\s+mais\s+r[aá]pido|the\s+fastest|garantido|guaranteed|100%\s+(seguro|safe|eficaz))\b"),
]
COMPANY = [("CNPJ", r"\bCNPJ\b|\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}"), ("endereço", r"\bCEP\b|\d{5}-\d{3}|\b(rua|avenida|av\.|alameda|street|st\.)\b"), ("contato", r"mailto:|tel:|whatsapp|contato@|contact@|support@|suporte@")]
UNSUB = r"unsubscribe|descadastr|cancelar\s+(a\s+)?inscri[cç][aã]o|n[aã]o\s+quero\s+mais\s+receber|List-Unsubscribe|\{\{\s*unsubscribe"
DELETE = r"(excluir|apagar|deletar|delete|remover|remove)\s+(minha\s+|my\s+)?(conta|account|dados|data)|data-deletion|account-deletion|exclus[aã]o\s+de\s+dados"
AGE = r"age(-|_)?(gate|verification|check)|maior\s+de\s+(idade|18)|tenho\s+18|over\s+18|verifica[cç][aã]o\s+de\s+idade|parental|respons[aá]vel\s+legal"
SENSITIVE_FIELDS = r"name=[\"'](cpf|rg|cnh|passport|passaporte|birth(date|day)|data_nascimento|nascimento|ssn|religi|orienta|etnia|race|health|saude|biometr)"


def files(root):
    for d, dirs, fs in os.walk(root):
        dirs[:] = [x for x in dirs if x not in SKIP]
        for f in fs:
            if os.path.splitext(f)[1].lower() in EXTS:
                yield os.path.join(d, f)


def grep(paths, rx, flags=re.I):
    r = re.compile(rx, flags); hits = []
    for p in paths:
        try:
            with open(p, encoding="utf-8", errors="ignore") as fh:
                for n, line in enumerate(fh, 1):
                    if r.search(line): hits.append((p, n, line.strip()[:120]))
        except OSError:
            pass
    return hits


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    as_json = "--json" in sys.argv
    emails = None
    if "--emails" in sys.argv:
        i = sys.argv.index("--emails"); emails = sys.argv[i + 1]; args = [a for a in args if a != emails]
    if not args: print(__doc__); sys.exit(2)
    root = args[0]; paths = list(files(root)); R = {"findings": [], "third_party": [], "legal": {}}
    def add(sev, area, msg, where=None): R["findings"].append({"sev": sev, "area": area, "msg": msg, "where": where})

    # Documentos legais: rota/arquivo OU link
    for key, rx in LEGAL.items():
        by_path = [p for p in paths if re.search(rx, os.path.basename(p), re.I) or re.search(rx, os.path.dirname(p).split(os.sep)[-1], re.I)]
        by_link = grep(paths, r"href=[\"'][^\"']*(" + rx + r")")
        R["legal"][key] = {"paginas": by_path[:5], "links": len(by_link)}
        if not by_path and not by_link: add("FALHA", "legal", f"nenhuma página nem link de {key} encontrado")
        elif by_path and not by_link: add("AVISO", "legal", f"página de {key} existe mas nenhum link para ela (rodapé/cadastro?)", by_path[0])
    upd = grep(paths, r"[uú]ltima\s+atualiza[cç][aã]o|last\s+updated|atualizado\s+em")
    if R["legal"] and not upd: add("AVISO", "legal", "documentos sem 'última atualização' detectável")

    # Consentimento de cookies e terceiros
    consent = grep(paths, CONSENT)
    tp_hits = []
    for name, rx in THIRD_PARTY:
        h = grep(paths, rx)
        if h: tp_hits.append((name, h)); R["third_party"].append({"sdk": name, "ocorrencias": len(h), "exemplo": f"{h[0][0]}:{h[0][1]}"})
    if tp_hits and not consent: add("FALHA", "cookies", f"{len(tp_hits)} SDK(s) de terceiros e nenhum componente de consentimento de cookies detectado")
    elif tp_hits:
        # scripts estáticos em <head>/<script src> indicam carga antes do consentimento
        static = [(n, h) for n, h in tp_hits if any(re.search(r"<script[^>]*src=|<link[^>]*href=", l) for _, _, l in h) and "font" not in n.lower()]
        for n, h in static: add("FALHA", "cookies", f"{n} carregado via <script>/<link> estático — provavelmente antes do consentimento", f"{h[0][0]}:{h[0][1]}")
    if not consent and not tp_hits: add("AVISO", "cookies", "sem consentimento de cookies nem terceiros detectados — se só há cookies essenciais, documente na política")
    rej = grep(paths, r"rejeitar|recusar|reject\s+all|decline|somente\s+(os\s+)?(essenciais|necess)|only\s+(essential|necessary)")
    if consent and not rej: add("FALHA", "cookies", "consentimento sem botão de Rejeitar/Somente essenciais detectável")
    prefs = grep(paths, r"prefer[eê]ncias\s+de\s+cookies|cookie\s+(settings|preferences)|gerenciar\s+cookies|manage\s+cookies")
    if consent and not prefs: add("AVISO", "cookies", "sem link para reabrir preferências de cookies")

    # Formulários
    forms = grep(paths, r"<form\b|<Form\b|useForm\(|onSubmit=")
    if forms:
        cons = grep(paths, r"type=[\"']checkbox[\"'][^>]*(consent|aceito|concordo|agree|terms|termos|privac|newsletter|marketing)|(consent|aceito|concordo|agree|termos|terms)[^<]{0,80}type=[\"']checkbox")
        if not cons: add("FALHA", "formularios", f"{len(forms)} formulário(s) e nenhum checkbox de consentimento detectável (termos/privacidade/newsletter)")
        pre = grep(paths, r"type=[\"']checkbox[\"'][^>]*\b(checked|defaultChecked)\b(?!=\{?false)|\b(checked|defaultChecked)\b(?!=\{?false)[^>]*type=[\"']checkbox")
        for p, n, l in pre[:10]: add("FALHA", "formularios", "checkbox pré-marcado (consentimento deve ser opt-in)", f"{p}:{n}")
        sens = grep(paths, SENSITIVE_FIELDS)
        for p, n, l in sens[:10]: add("AVISO", "dados", "campo de dado sensível/identificador — justifique a finalidade na política", f"{p}:{n}")
        nolabel = grep(paths, r"<input\b(?![^>]*type=[\"'](hidden|submit|button|checkbox|radio))(?![^>]*(aria-label|id=|aria-labelledby))")
        if nolabel: add("AVISO", "a11y", f"{len(nolabel)} <input> sem id/aria-label (label associado?)", f"{nolabel[0][0]}:{nolabel[0][1]}")

    # Exclusão de dados, menores, empresa
    if not grep(paths, DELETE): add("FALHA", "dados", "nenhuma opção/menção de exclusão de conta e dados encontrada")
    if not grep(paths, AGE): add("AVISO", "menores", "sem verificação de idade/consentimento parental — N/A se o público é só adulto e não há conteúdo restrito")
    for name, rx in COMPANY:
        if not grep(paths, rx): add("AVISO" if name != "contato" else "FALHA", "empresa", f"dados da empresa: {name} não encontrado (rodapé/contato)")

    # Alt, contraste (só heurística de opacidade), teclado
    noalt = grep(paths, r"<img\b(?![^>]*\balt=)|<Image\b(?![^>]*\balt=)")
    if noalt: add("FALHA", "a11y", f"{len(noalt)} imagem(ns) sem alt", f"{noalt[0][0]}:{noalt[0][1]}")
    outline = grep(paths, r"outline:\s*none|outline-none(?!\s*focus-visible)|focus:outline-none(?![^\"']*focus(-visible)?:ring)")
    if outline: add("FALHA", "a11y", f"{len(outline)} remoção de outline sem substituto de foco visível", f"{outline[0][0]}:{outline[0][1]}")
    clickdiv = grep(paths, r"<div[^>]*onClick|<span[^>]*onClick|<div[^>]*@click")
    if clickdiv: add("AVISO", "a11y", f"{len(clickdiv)} div/span clicável — use <button>/<a> para teclado", f"{clickdiv[0][0]}:{clickdiv[0][1]}")
    if not grep(paths, r"skip(-|\s)?(to|link|nav)|pular\s+para\s+o\s+conte"): add("AVISO", "a11y", "sem skip link detectável")
    if not grep(paths, r"prefers-reduced-motion|useReducedMotion|respectReducedMotion"): add("AVISO", "a11y", "prefers-reduced-motion não tratado")
    lowc = grep(paths, r"text-(gray|zinc|neutral|slate|stone)-(300|400)\b|text-white/[1-5]0\b|opacity-[1-5]0\b[^\"']*text")
    if lowc: add("AVISO", "a11y", f"{len(lowc)} texto(s) com cor clara/opacidade — verifique contraste ≥ 4.5:1", f"{lowc[0][0]}:{lowc[0][1]}")

    # Dark patterns e alegações
    for name, rx in DARK:
        h = grep(paths, rx)
        for p, n, l in h[:5]: add("AVISO" if name in ("pré-seleção", "prova social vaga") else "FALHA", "honestidade", f"{name}: {l}", f"{p}:{n}")
    fees = grep(paths, r"a\s+partir\s+de|starting\s+at|from\s+\$|\+\s*(taxas|fees|impostos|taxes)|taxa\s+de\s+(conveni|servi)")
    for p, n, l in fees[:5]: add("AVISO", "honestidade", f"preço parcial/taxa adicional — total visível antes do último passo? {l}", f"{p}:{n}")

    # Licenças de imagens
    if not any(os.path.exists(os.path.join(root, x)) for x in ("docs/licenses/images.md", "docs/licenses", "LICENSES.md", "docs/LICENSES.md")):
        add("AVISO", "licencas", "sem registro de licenças de imagens (docs/licenses/images.md)")

    # E-mails
    if emails:
        epaths = list(files(emails)); R["emails"] = len(epaths)
        for p in epaths:
            body = open(p, encoding="utf-8", errors="ignore").read()
            is_marketing = re.search(r"newsletter|promo|oferta|offer|campaign|campanha|desconto|discount|novidades", body, re.I)
            if is_marketing and not re.search(UNSUB, body, re.I): add("FALHA", "emails", "template de marketing sem link de descadastro", p)
            if not re.search(r"\b(rua|av\.|avenida|street|cep|\d{5}-\d{3})\b", body, re.I): add("AVISO", "emails", "template sem endereço físico do remetente", p)
            if re.search(r"<img\b(?![^>]*\balt=)", body, re.I): add("AVISO", "emails", "imagem sem alt no e-mail", p)

    if as_json: print(json.dumps(R, ensure_ascii=False, indent=1)); return
    nf = sum(1 for f in R["findings"] if f["sev"] == "FALHA")
    print(f"# compliance check — {len(paths)} arquivos, {nf} falha(s), {len(R['third_party'])} SDK(s) de terceiros\n")
    print("## Documentos legais")
    for k, v in R["legal"].items(): print(f"- {k}: {'página ' + os.path.relpath(v['paginas'][0], root) if v['paginas'] else 'sem página'} · {v['links']} link(s)")
    print("\n## Terceiros detectados (inventariar: finalidade, dados, categoria de consentimento)")
    for t in R["third_party"]: print(f"- {t['sdk']}: {t['ocorrencias']}× (ex.: {t['exemplo']})")
    if not R["third_party"]: print("- nenhum")
    print("\n## Achados")
    for area in sorted({f["area"] for f in R["findings"]}):
        print(f"### {area}")
        for f in R["findings"]:
            if f["area"] == area: print(f"- [{f['sev']}] {f['msg']}" + (f" — {f['where']}" if f["where"] else ""))
    print("\nHeurístico. Confirme na página renderizada e, com Playwright, teste consentimento, teclado e exclusão de conta. Texto legal final: revisão humana (needs-human).")
    sys.exit(1 if nf else 0)


if __name__ == "__main__":
    main()
