#!/usr/bin/env python3
"""Checker estático de SEO para HTML renderizado.

Uso: python3 seo_check.py <arquivo.html | pasta> [--base https://site.com] [--json]
Sem dependências (html.parser da stdlib). Valida head, headings, imagens, links,
JSON-LD, OG, hreflang por página; e robots.txt / sitemap.xml na pasta.
Não mede performance: use Lighthouse/CrUX para Core Web Vitals.
"""
import json
import os
import re
import sys
from collections import Counter
from html.parser import HTMLParser

SKIP_DIRS = {"node_modules", ".git", ".next/cache", "coverage", "__pycache__"}


class Page(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.title = None; self._in_title = False
        self.metas = []; self.links = []; self.headings = []; self.imgs = []
        self.anchors = []; self._anchor = None; self.jsonld = []; self._in_ld = False
        self.lang = None; self._h = None; self.scripts_ext = []; self.iframes = 0

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        if tag == "html": self.lang = a.get("lang")
        elif tag == "title": self._in_title = True; self.title = self.title or ""
        elif tag == "meta": self.metas.append(a)
        elif tag == "link": self.links.append(a)
        elif tag in ("h1", "h2", "h3", "h4", "h5", "h6"): self._h = [tag, ""]
        elif tag == "img": self.imgs.append(a)
        elif tag == "a": self._anchor = [a, ""]
        elif tag == "script":
            if (a.get("type") or "").lower() == "application/ld+json": self._in_ld = True; self.jsonld.append("")
            elif a.get("src"): self.scripts_ext.append(a["src"])
        elif tag == "iframe": self.iframes += 1

    def handle_endtag(self, tag):
        if tag == "title": self._in_title = False
        elif tag.startswith("h") and self._h and tag == self._h[0]: self.headings.append((tag, self._h[1].strip())); self._h = None
        elif tag == "a" and self._anchor: self.anchors.append((self._anchor[0], self._anchor[1].strip())); self._anchor = None
        elif tag == "script": self._in_ld = False

    def handle_data(self, d):
        if self._in_title: self.title += d
        if self._h: self._h[1] += d
        if self._anchor: self._anchor[1] += d
        if self._in_ld: self.jsonld[-1] += d


def meta(p, name=None, prop=None):
    for m in p.metas:
        if name and (m.get("name") or "").lower() == name: return m.get("content")
        if prop and (m.get("property") or "").lower() == prop: return m.get("content")
    return None


def check_page(path, html):
    p = Page(); p.feed(html)
    F = []  # (sev, msg)
    def fail(m): F.append(("FALHA", m))
    def warn(m): F.append(("AVISO", m))

    robots = (meta(p, name="robots") or "").lower()
    noindex = "noindex" in robots
    if noindex: warn("página noindex — confirme que é intencional (área logada, busca interna, obrigado)")

    t = (p.title or "").strip()
    if not t: fail("sem <title>")
    elif not 15 <= len(t) <= 60: warn(f"<title> com {len(t)} chars (ideal 15–60): {t[:70]}")
    d = meta(p, name="description")
    if not d: fail("sem meta description")
    elif not 50 <= len(d) <= 160: warn(f"meta description com {len(d)} chars (ideal 50–160)")
    if not p.lang: fail("<html> sem lang")
    if not meta(p, name="viewport"): fail("sem meta viewport (mobile-first)")
    canon = [l for l in p.links if (l.get("rel") or "").lower() == "canonical"]
    if not canon and not noindex: fail("sem <link rel=canonical>")
    elif canon and not (canon[0].get("href") or "").startswith("http"): warn("canonical não é URL absoluta")
    if len(canon) > 1: fail("mais de um canonical")

    h1 = [h for h in p.headings if h[0] == "h1"]
    if len(h1) == 0: fail("sem <h1>")
    elif len(h1) > 1: fail(f"{len(h1)} <h1> (deve ser 1)")
    last = 0
    for tag, _ in p.headings:
        lvl = int(tag[1])
        if last and lvl > last + 1: warn(f"heading pula nível: h{last} → {tag}"); break
        last = lvl
    empties = [h for h in p.headings if not h[1]]
    if empties: warn(f"{len(empties)} heading(s) vazio(s)")

    noalt = [i for i in p.imgs if "alt" not in i]
    if noalt: fail(f"{len(noalt)} <img> sem atributo alt (use alt=\"\" se decorativa)")
    nodim = [i for i in p.imgs if not (("width" in i and "height" in i) or "style" in i)]
    if nodim: warn(f"{len(nodim)} <img> sem width/height (risco de CLS)")
    lazy_first = [i for i in p.imgs[:1] if (i.get("loading") or "").lower() == "lazy"]
    if lazy_first: warn("primeira imagem com loading=lazy — se for a LCP, remova e use fetchpriority=high")
    badalt = [i for i in p.imgs if re.match(r"^\s*(imagem|image|picture|foto|photo)\b", (i.get("alt") or ""), re.I)]
    if badalt: warn(f"{len(badalt)} alt começando com 'imagem de/image of'")

    generic = [a for a in p.anchors if a[1].lower() in {"clique aqui", "click here", "saiba mais", "learn more", "aqui", "here", "read more", "leia mais"}]
    if generic: warn(f"{len(generic)} link(s) com âncora genérica (clique aqui / saiba mais)")
    noText = [a for a in p.anchors if not a[1] and "aria-label" not in a[0] and "title" not in a[0]]
    if noText: warn(f"{len(noText)} link(s) sem texto nem aria-label")
    hashlinks = [a for a in p.anchors if (a[0].get("href") or "") in ("#", "", "javascript:void(0)")]
    if hashlinks: warn(f"{len(hashlinks)} link(s) com href vazio/#/javascript: — não rastreável")

    for i, raw in enumerate(p.jsonld):
        try:
            data = json.loads(raw)
            items = data if isinstance(data, list) else [data]
            for it in items:
                if isinstance(it, dict) and "@graph" in it: items = it["@graph"]; break
            types = [it.get("@type") for it in items if isinstance(it, dict)]
            if not any(types): fail(f"JSON-LD #{i+1} sem @type")
            if not any(isinstance(it, dict) and "@context" in it for it in ([data] if isinstance(data, dict) else data)): warn(f"JSON-LD #{i+1} sem @context")
            for it in items:
                if isinstance(it, dict) and it.get("@type") in ("Product", "Article", "BlogPosting", "Organization", "LocalBusiness") and "aggregateRating" in it:
                    warn(f"JSON-LD {it.get('@type')} com aggregateRating — só com avaliações reais e visíveis")
        except json.JSONDecodeError as e:
            fail(f"JSON-LD #{i+1} inválido: {e}")
    if not p.jsonld and not noindex: warn("sem dados estruturados (JSON-LD)")

    for k in ("og:title", "og:description", "og:image", "og:url"):
        if not meta(p, prop=k): warn(f"sem {k}")
    if not meta(p, name="twitter:card"): warn("sem twitter:card")

    hre = [l for l in p.links if (l.get("rel") or "").lower() == "alternate" and l.get("hreflang")]
    if hre:
        langs = [l["hreflang"].lower() for l in hre]
        if "x-default" not in langs: warn("hreflang sem x-default")
        if any(not (l.get("href") or "").startswith("http") for l in hre): warn("hreflang com href relativo")
    if p.iframes: warn(f"{p.iframes} iframe(s) — conteúdo essencial não deve estar em iframe")
    if len(p.scripts_ext) > 12: warn(f"{len(p.scripts_ext)} scripts externos — audite terceiros (INP/supply chain)")
    text_len = len(re.sub(r"<[^>]+>", " ", html))
    if text_len < 1500 and not noindex: warn("pouco conteúdo textual no HTML servido — confira se o principal depende de JS")

    return {"page": path, "title": t, "description": d, "noindex": noindex, "findings": F}


def check_site(root):
    F = []
    rb = os.path.join(root, "robots.txt")
    if not os.path.exists(rb): F.append(("AVISO", "robots.txt ausente na raiz"))
    else:
        body = open(rb, encoding="utf-8", errors="ignore").read()
        if re.search(r"^Disallow:\s*/\s*$", body, re.M): F.append(("FALHA", "robots.txt bloqueia o site inteiro (Disallow: /)"))
        if "sitemap:" not in body.lower(): F.append(("AVISO", "robots.txt não aponta para o sitemap"))
    sm = os.path.join(root, "sitemap.xml")
    if not os.path.exists(sm): F.append(("AVISO", "sitemap.xml ausente na raiz"))
    else:
        body = open(sm, encoding="utf-8", errors="ignore").read()
        locs = re.findall(r"<loc>\s*([^<\s]+)\s*</loc>", body)
        if not locs: F.append(("FALHA", "sitemap.xml sem <loc>"))
        if len(locs) > 50000: F.append(("FALHA", f"sitemap com {len(locs)} URLs (> 50.000)"))
        if any(not u.startswith("http") for u in locs): F.append(("FALHA", "sitemap com URL relativa"))
        if any(u.startswith("http://") for u in locs): F.append(("AVISO", "sitemap com URLs http://"))
    if not any(os.path.exists(os.path.join(root, n)) for n in ("404.html", "404/index.html", "not-found.html")):
        F.append(("AVISO", "sem página 404 própria encontrada (404.html)"))
    return F


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    as_json = "--json" in sys.argv
    if not args: print(__doc__); sys.exit(2)
    root = args[0]
    pages = []
    if os.path.isfile(root):
        pages.append(check_page(root, open(root, encoding="utf-8", errors="ignore").read()))
        site = []
    else:
        for d, dirs, files in os.walk(root):
            dirs[:] = [x for x in dirs if x not in SKIP_DIRS]
            for f in files:
                if f.endswith((".html", ".htm")):
                    pth = os.path.join(d, f)
                    pages.append(check_page(pth, open(pth, encoding="utf-8", errors="ignore").read()))
        site = check_site(root)
        titles = Counter(p["title"] for p in pages if p["title"] and not p["noindex"])
        descs = Counter(p["description"] for p in pages if p["description"] and not p["noindex"])
        for t, c in titles.items():
            if c > 1: site.append(("FALHA", f"<title> duplicado em {c} páginas: {t[:60]}"))
        for dsc, c in descs.items():
            if c > 1: site.append(("AVISO", f"meta description duplicada em {c} páginas"))
    if as_json:
        print(json.dumps({"site": site, "pages": pages}, ensure_ascii=False, indent=1)); return
    nf = sum(1 for p in pages for s, _ in p["findings"] if s == "FALHA") + sum(1 for s, _ in site if s == "FALHA")
    print(f"# seo check — {len(pages)} página(s), {nf} falha(s)\n")
    if site:
        print("## Site")
        for s, m in site: print(f"- [{s}] {m}")
        print()
    for p in pages:
        print(f"## {p['page']}" + (" (noindex)" if p["noindex"] else ""))
        for s, m in p["findings"]: print(f"- [{s}] {m}")
        if not p["findings"]: print("- ok")
        print()
    print("Estático: Core Web Vitals (LCP/INP/CLS) exigem Lighthouse (lab) e Search Console/CrUX (campo).")
    sys.exit(1 if nf else 0)


if __name__ == "__main__":
    main()
