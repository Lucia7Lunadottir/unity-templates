#!/usr/bin/env python3
"""
Генератор статических HTML-страниц документации (тема Catppuccin Mocha: сайдбар
слева, on-page оглавление справа, pager снизу, подсветка синтаксиса в блоках кода)
из простого подмножества Markdown. Без внешних зависимостей - только стандартная
библиотека, чтобы работать в любом окружении без pip install.

Использование:
    python3 gen_docs.py

Берёт .md-файлы, перечисленные в MANIFEST ниже, рядом со скриптом, и пишет
одноимённые .html туда же. MANIFEST задаёт порядок страниц, подписи в сайдбаре,
группировку по разделам (part_label) и pager (проставляется автоматически по
порядку MANIFEST).

Поддерживаемое подмножество Markdown (сознательно урезано под собственные нужды
этой документации, не общий парсер):
  - Заголовки # / ## / ### (только 3 уровня - как в исходном шаблоне)
  - Абзацы (пустая строка - разделитель)
  - **жирный**, `код`, [текст](ссылка)
  - Списки: каждый пункт - ОДНА строка (без переноса пункта на несколько строк)
  - Таблицы GFM (| a | b | + строка-разделитель |---|---|)
  - Блок кода ```csharp ... ``` (простая C#/HLSL-подсветка по токенам)
  - Цитата (> текст)
  - Горизонтальная черта (---) на отдельной строке
"""
import html
import os
import re

SITE_TITLE = "VFX Pool: эффекты из пула"
BRAND = "Catppuccin Mocha"

# (slug, ярлык в сайдбаре, part_label или None - "None" значит "тот же раздел, что у предыдущей строки")
MANIFEST = [
    ("index", "Обзор", "Начало"),
    ("01-pool-and-anchor", "Пул и пустышка", "Как устроено"),
    ("02-converter", "Перевод ParticlesInvoker на пул", None),
]

CS_KEYWORDS = (
    "new using public private protected internal class struct void return if else for "
    "foreach while static readonly var this namespace override sealed out ref in int float "
    "double bool string true false null default get set enum switch case break continue "
    "try catch finally throw event delegate params base abstract virtual const async await "
    "yield is as typeof implicit explicit"
).split()

TOKEN_RE = re.compile(
    r"(?P<com>//[^\n]*)"
    r"|(?P<str>\"(?:[^\"\\]|\\.)*\")"
    r"|(?P<kw>\b(?:" + "|".join(CS_KEYWORDS) + r")\b)"
    r"|(?P<type>\b[A-Z][A-Za-z0-9_]*\b)"
)

TEMPLATE = """<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>$TITLE — $SITE_TITLE</title>
<style>
:root {
  --base: #1e1e2e; --mantle: #181825; --crust: #11111b;
  --surface0: #313244; --surface1: #45475a; --surface2: #585b70;
  --overlay0: #6c7086; --overlay1: #7f849c;
  --text: #cdd6f4; --subtext1: #bac2de; --subtext0: #a6adc8;
  --rosewater: #f5e0dc; --flamingo: #f2cdcd; --pink: #f5c2e7; --mauve: #cba6f7;
  --red: #f38ba8; --maroon: #eba0ac; --peach: #fab387; --yellow: #f9e2af;
  --green: #a6e3a1; --teal: #94e2d5; --sky: #89dceb; --sapphire: #74c7ec;
  --blue: #89b4fa; --lavender: #b4befe;
}
* { box-sizing: border-box; }
html { scroll-behavior: smooth; }
body {
  margin: 0; background: var(--base); color: var(--text);
  font-family: -apple-system, "Segoe UI", "Noto Sans", "Helvetica Neue", Arial, sans-serif;
  line-height: 1.7; font-size: 16px;
}
.layout {
  display: grid;
  grid-template-columns: 260px minmax(0, 1fr) 220px;
  max-width: 1400px;
  margin: 0 auto;
}
.sidebar {
  position: sticky; top: 0; align-self: start; height: 100vh; overflow-y: auto;
  padding: 1.8rem 1.2rem; border-right: 1px solid var(--surface1);
}
.sidebar .brand {
  font-size: 0.72rem; text-transform: uppercase; letter-spacing: 0.06em;
  color: var(--crust); background: var(--mauve); display: inline-block;
  padding: 0.15rem 0.6rem; border-radius: 999px; font-weight: 700; margin-bottom: 1.4rem;
}
.sidebar nav a {
  display: block; padding: 0.5rem 0.7rem; margin: 0.1rem 0; border-radius: 7px;
  color: var(--subtext1); text-decoration: none; font-size: 0.92rem; border-bottom: none;
  transition: background 0.12s, color 0.12s;
}
.sidebar nav a:hover { background: var(--surface0); color: var(--text); }
.sidebar nav a.active { background: var(--mauve); color: var(--crust); font-weight: 700; }
.sidebar nav .part-label {
  font-size: 0.68rem; text-transform: uppercase; letter-spacing: 0.07em; color: var(--overlay0);
  margin: 1.1rem 0 0.3rem 0.7rem;
}
.content { padding: 2.6rem 2.6rem 5rem; min-width: 0; }
.onpage {
  position: sticky; top: 0; align-self: start; height: 100vh; overflow-y: auto;
  padding: 2.6rem 1.2rem;
}
.onpage .label {
  font-size: 0.68rem; text-transform: uppercase; letter-spacing: 0.07em; color: var(--overlay0);
  margin-bottom: 0.6rem;
}
.onpage a {
  display: block; padding: 0.28rem 0; color: var(--subtext0); text-decoration: none;
  font-size: 0.86rem; border-bottom: none; border-left: 2px solid transparent; padding-left: 0.7rem;
}
.onpage a.h3 { padding-left: 1.4rem; font-size: 0.82rem; }
.onpage a:hover { color: var(--sapphire); border-left-color: var(--sapphire); }

h1, h2, h3 { font-weight: 700; line-height: 1.3; scroll-margin-top: 1.5rem; }
h1 { font-size: 2rem; color: var(--mauve); margin: 0 0 0.6rem; }
.page-kicker {
  font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.08em; color: var(--peach);
  font-weight: 700; margin-bottom: 0.5rem;
}
h2 { font-size: 1.4rem; color: var(--blue); margin: 2.4rem 0 1rem; padding-bottom: 0.4rem; border-bottom: 1px solid var(--surface1); }
h3 { font-size: 1.1rem; color: var(--sky); margin: 1.8rem 0 0.8rem; }
p { margin: 0.9rem 0; color: var(--text); }
a { color: var(--sapphire); text-decoration: none; border-bottom: 1px solid rgba(116, 199, 236, 0.35); }
a:hover { color: var(--lavender); border-bottom-color: var(--lavender); }
strong { color: var(--peach); font-weight: 700; }
code {
  font-family: "JetBrains Mono", "Fira Code", Consolas, "Liberation Mono", monospace;
  font-size: 0.86em; background: var(--surface0); color: var(--peach);
  padding: 0.15em 0.4em; border-radius: 5px;
}
pre.code {
  background: var(--mantle); border: 1px solid var(--surface1); border-radius: 10px;
  padding: 1rem 1.2rem; overflow-x: auto; margin: 1.1rem 0;
}
pre.code code { background: none; color: var(--text); padding: 0; font-size: 0.85em; border-radius: 0; }
.tok-kw { color: var(--mauve); font-weight: 600; }
.tok-type { color: var(--yellow); }
.tok-str { color: var(--green); }
.tok-com { color: var(--overlay0); font-style: italic; }
blockquote {
  margin: 1.1rem 0; padding: 0.15rem 1.1rem; border-left: 3px solid var(--peach);
  background: var(--surface0); border-radius: 0 8px 8px 0; color: var(--subtext1);
}
blockquote p { margin: 0.6rem 0; }
ul, ol { padding-left: 1.4rem; margin: 0.9rem 0; }
li { margin: 0.45rem 0; }
hr { border: none; border-top: 1px solid var(--surface1); margin: 2.2rem 0; }
.table-wrap { overflow-x: auto; margin: 1.1rem 0; }
table { border-collapse: collapse; width: 100%; font-size: 0.92em; }
th, td { border: 1px solid var(--surface1); padding: 0.55rem 0.8rem; text-align: left; vertical-align: top; }
th { background: var(--surface0); color: var(--lavender); }
tbody tr:nth-child(odd) { background: rgba(49, 50, 68, 0.35); }
.cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; margin: 1.5rem 0; }
.card {
  display: block; padding: 1.1rem 1.2rem; background: var(--mantle); border: 1px solid var(--surface1);
  border-radius: 12px; text-decoration: none; border-bottom: 1px solid var(--surface1); transition: border-color 0.12s, transform 0.12s;
}
.card:hover { border-color: var(--mauve); transform: translateY(-2px); }
.card .card-title { color: var(--text); font-weight: 700; font-size: 1rem; margin-bottom: 0.3rem; display: block; }
.card .card-desc { color: var(--subtext0); font-size: 0.86rem; }
.pager { display: flex; justify-content: space-between; margin-top: 3.5rem; padding-top: 1.5rem; border-top: 1px solid var(--surface1); gap: 1rem; }
.pager a {
  border: 1px solid var(--surface1); border-radius: 10px; padding: 0.8rem 1.1rem; flex: 1;
  border-bottom: 1px solid var(--surface1); color: var(--text);
}
.pager a:hover { border-color: var(--mauve); }
.pager .dir { font-size: 0.72rem; text-transform: uppercase; color: var(--overlay0); display: block; margin-bottom: 0.2rem; }
.pager .next { text-align: right; }
::selection { background: var(--surface2); color: var(--rosewater); }
@media (max-width: 1100px) {
  .layout { grid-template-columns: 220px minmax(0, 1fr); }
  .onpage { display: none; }
}
@media (max-width: 760px) {
  .layout { grid-template-columns: 1fr; }
  .sidebar { position: relative; height: auto; border-right: none; border-bottom: 1px solid var(--surface1); }
  .content { padding: 1.6rem 1.2rem 3rem; }
}
</style>
</head>
<body>
<div class="layout">
<aside class="sidebar">
  <span class="brand">$BRAND</span>
  <nav>$SIDEBAR</nav>
</aside>
<main class="content">
<div class="page-kicker">$KICKER</div>
$BODY
$PAGER
</main>
<aside class="onpage">
  <div class="label">На этой странице</div>
$ONPAGE
</aside>
</div>
</body>
</html>
"""


def slugify(text):
    text = re.sub(r"<[^>]+>", "", text)
    s = text.lower()
    s = re.sub(r"[^a-zа-яё0-9\s-]", "", s)
    s = re.sub(r"\s+", "-", s.strip())
    s = re.sub(r"-{2,}", "-", s)
    return s


def highlight_code(code):
    escaped = html.escape(code, quote=False)

    def repl(m):
        return '<span class="tok-%s">%s</span>' % (m.lastgroup, m.group(0))

    return TOKEN_RE.sub(repl, escaped)


def inline(text):
    text = html.escape(text, quote=False)
    codes = []

    def stash(m):
        codes.append(m.group(1))
        return "\x00CODE%d\x00" % (len(codes) - 1)

    text = re.sub(r"`([^`]+)`", stash, text)
    text = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", r'<a href="\2">\1</a>', text)
    text = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", text)

    def restore(m):
        return "<code>%s</code>" % codes[int(m.group(1))]

    text = re.sub(r"\x00CODE(\d+)\x00", restore, text)
    return text


def md_to_html(md_text):
    lines = md_text.strip("\n").split("\n")
    i, n = 0, len(lines)
    out = []

    while i < n:
        line = lines[i]
        if line.strip() == "":
            i += 1
            continue

        m = re.match(r"^(#{1,3})\s+(.*)$", line)
        if m:
            level = len(m.group(1))
            text = m.group(2).strip()
            slug = slugify(text)
            out.append('<h%d id="%s">%s</h%d>' % (level, slug, inline(text), level))
            i += 1
            continue

        m = re.match(r"^```(\w*)\s*$", line)
        if m:
            lang = m.group(1) or "text"
            i += 1
            code_lines = []
            while i < n and not re.match(r"^```\s*$", lines[i]):
                code_lines.append(lines[i])
                i += 1
            i += 1
            code = "\n".join(code_lines)
            out.append('<pre class="code lang-%s"><code>%s</code></pre>' % (lang, highlight_code(code)))
            continue

        if line.startswith("> "):
            quote_lines = []
            while i < n and lines[i].startswith("> "):
                quote_lines.append(lines[i][2:])
                i += 1
            out.append("<blockquote><p>%s</p></blockquote>" % inline(" ".join(quote_lines)))
            continue

        if re.match(r"^-{3,}$", line.strip()):
            out.append("<hr>")
            i += 1
            continue

        if line.strip().startswith("|") and i + 1 < n and re.match(r"^\|?[\s:|-]+\|?$", lines[i + 1].strip()):
            header_cells = [c.strip() for c in line.strip().strip("|").split("|")]
            i += 2
            rows = []
            while i < n and lines[i].strip().startswith("|"):
                rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
                i += 1
            thead = "<tr>" + "".join("<th>%s</th>" % inline(c) for c in header_cells) + "</tr>"
            tbody = "".join(
                "<tr>" + "".join("<td>%s</td>" % inline(c) for c in row) + "</tr>" for row in rows
            )
            out.append('<div class="table-wrap"><table><thead>%s</thead><tbody>%s</tbody></table></div>' % (thead, tbody))
            continue

        if re.match(r"^[-*]\s+", line):
            items = []
            while i < n and re.match(r"^[-*]\s+", lines[i]):
                items.append(re.sub(r"^[-*]\s+", "", lines[i]))
                i += 1
            out.append("<ul>" + "".join("<li>%s</li>" % inline(it) for it in items) + "</ul>")
            continue

        if re.match(r"^\d+\.\s+", line):
            items = []
            while i < n and re.match(r"^\d+\.\s+", lines[i]):
                items.append(re.sub(r"^\d+\.\s+", "", lines[i]))
                i += 1
            out.append("<ol>" + "".join("<li>%s</li>" % inline(it) for it in items) + "</ol>")
            continue

        para_lines = []
        while (
            i < n
            and lines[i].strip() != ""
            and not re.match(r"^(#{1,3})\s", lines[i])
            and not lines[i].startswith("```")
            and not lines[i].startswith("> ")
            and not re.match(r"^[-*]\s+", lines[i])
            and not re.match(r"^\d+\.\s+", lines[i])
            and not lines[i].strip().startswith("|")
            and not re.match(r"^-{3,}$", lines[i].strip())
        ):
            para_lines.append(lines[i].strip())
            i += 1
        out.append("<p>%s</p>" % inline(" ".join(para_lines)))

    return "\n".join(out)


def build_sidebar(current_slug):
    out = []
    last_part = None
    for slug, label, part in MANIFEST:
        if part is not None and part != last_part:
            out.append('<div class="part-label">%s</div>' % html.escape(part))
            last_part = part
        active = " active" if slug == current_slug else ""
        out.append('<a class="%s" href="%s.html">%s</a>' % (active.strip(), slug, html.escape(label)))
    return "\n".join(out)


def build_onpage(body_html):
    out = []
    for m in re.finditer(r'<h([23]) id="([^"]+)">(.*?)</h\1>', body_html):
        level, slug, text = m.group(1), m.group(2), re.sub(r"<[^>]+>", "", m.group(3))
        cls = " h3" if level == "3" else ""
        out.append('<a class="%s" href="#%s">%s</a>' % (cls.strip(), slug, text))
    return "\n".join(out)


def current_part(slug):
    last = None
    for s, _label, part in MANIFEST:
        if part is not None:
            last = part
        if s == slug:
            return last
    return ""


def build_pager(index):
    prev_html = "<div></div>"
    next_html = "<div></div>"
    if index > 0:
        pslug, plabel, _ = MANIFEST[index - 1]
        prev_html = '<a class="prev" href="%s.html"><span class="dir">Назад</span>%s</a>' % (pslug, html.escape(plabel))
    if index < len(MANIFEST) - 1:
        nslug, nlabel, _ = MANIFEST[index + 1]
        next_html = '<a class="next" href="%s.html"><span class="dir">Дальше</span>%s</a>' % (nslug, html.escape(nlabel))
    return '<div class="pager">%s%s</div>' % (prev_html, next_html)


def extract_title(body_html):
    m = re.search(r"<h1[^>]*>(.*?)</h1>", body_html)
    return re.sub(r"<[^>]+>", "", m.group(1)) if m else SITE_TITLE


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    for index, (slug, _label, _part) in enumerate(MANIFEST):
        md_path = os.path.join(here, slug + ".md")
        if not os.path.isfile(md_path):
            print("SKIP (no .md): %s" % md_path)
            continue
        with open(md_path, "r", encoding="utf-8") as f:
            md_text = f.read()

        body_html = md_to_html(md_text)
        html_out = TEMPLATE
        html_out = html_out.replace("$TITLE", extract_title(body_html))
        html_out = html_out.replace("$SITE_TITLE", SITE_TITLE)
        html_out = html_out.replace("$BRAND", BRAND)
        html_out = html_out.replace("$SIDEBAR", build_sidebar(slug))
        html_out = html_out.replace("$KICKER", current_part(slug) or "")
        html_out = html_out.replace("$BODY", body_html)
        html_out = html_out.replace("$PAGER", build_pager(index))
        html_out = html_out.replace("$ONPAGE", build_onpage(body_html))

        out_path = os.path.join(here, slug + ".html")
        with open(out_path, "w", encoding="utf-8") as f:
            f.write(html_out)
        print("wrote %s" % out_path)


if __name__ == "__main__":
    main()
