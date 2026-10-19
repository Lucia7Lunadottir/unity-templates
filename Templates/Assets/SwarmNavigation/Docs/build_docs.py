#!/usr/bin/env python3
"""Собирает *.html страницы документации SwarmNav из *.md в этой же папке.

Шапка со стилями (Catppuccin Mocha) берётся из уже существующего 03-swarm-crowd.html, так что вид
страниц не меняется. Нужна библиотека markdown:  python3 -m venv .venv && .venv/bin/pip install markdown
Запуск:  .venv/bin/python build_docs.py
"""
import html
import os
import re

import markdown

HERE = os.path.dirname(os.path.abspath(__file__))

# (имя файла без расширения, подпись в навигации)
PAGES = [
    ("index", "Обзор"),
    ("01-quickstart", "Быстрый старт"),
    ("02-swarm-agent", "SwarmAgent (просто)"),
    ("03-swarm-crowd", "SwarmCrowd (Burst)"),
    ("04-how-it-works", "Как это работает"),
    ("05-advanced", "Расширенные темы"),
    ("06-SwarmNav-recipes", "Рецепты"),
    ("07-performance", "Производительность (GPU)"),
    ("08-behaviour-and-lod", "Поведение и LOD"),
    ("09-ai-integration", "Подключение ИИ"),
]


def slug(text):
    text = re.sub(r"<[^>]+>", "", html.unescape(text)).lower()
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


def fix_list_indent(md_text):
    """Приводит md к тому, что понимает Python-Markdown: 4 пробела для продолжения пункта
    нумерованного списка (в файлах - 3) и пустая строка перед списком, идущим сразу за абзацем."""
    out, in_ol, in_fence, fence_indent = [], False, False, 0
    for line in md_text.split("\n"):
        if line.lstrip().startswith("```"):
            if not in_fence:
                fence_indent = len(line) - len(line.lstrip())
            in_fence = not in_fence
            # блок кода с отступом внутри пункта списка Python-Markdown не распознаёт - снимаем отступ
            line = line[fence_indent:]
            out.append(line)
            continue
        if in_fence:
            out.append(line[fence_indent:] if line[:fence_indent].strip() == "" else line)
            continue
        if not in_fence and re.match(r"^([-*]|\d+\.) ", line) and out:
            previous = out[-1]
            if previous.strip() and not re.match(r"^(\s|[-*] |\d+\. )", previous):
                out.append("")
        if re.match(r"^\d+\. ", line):
            in_ol = True
        elif in_ol and line.strip() and not line.startswith(" "):
            in_ol = False
        if in_ol and re.match(r"^ {3}\S", line):
            line = " " + line
        out.append(line)
    return "\n".join(out)


def head_template():
    with open(os.path.join(HERE, "03-swarm-crowd.html"), encoding="utf8") as f:
        text = f.read()
    return text[: text.index("<body>")]


def render(md_text):
    body = markdown.markdown(fix_list_indent(md_text), extensions=["tables", "fenced_code"])
    body = re.sub(r"<pre><code[^>]*>", '<pre class="code"><code>', body)
    body = body.replace("<table>", '<div class="table-wrap"><table>').replace("</table>", "</table></div>")

    def add_id(match):
        tag, inner = match.group(1), match.group(2)
        return f'<{tag} id="{slug(inner)}">{inner}</{tag}>'

    return re.sub(r"<(h[123])>(.*?)</\1>", add_id, body, flags=re.S)


def build():
    head = head_template()
    for i, (name, label) in enumerate(PAGES):
        with open(os.path.join(HERE, name + ".md"), encoding="utf8") as f:
            md_text = f.read()
        content = render(md_text)
        h1 = re.search(r"<h1[^>]*>(.*?)</h1>", content, flags=re.S)
        title_text = re.sub(r"<[^>]+>", "", h1.group(1)) if h1 else label

        page_head = re.sub(r"<title>.*?</title>", f"<title>{html.escape(label)} — SwarmNav</title>", head, flags=re.S)

        nav = "".join(
            f'<a class="{"active" if n == name else ""}" href="{n}.html">{html.escape(l)}</a>\n' for n, l in PAGES
        )
        toc = "".join(
            f'<a class="" href="#{slug(m.group(1))}">{m.group(1)}</a>\n'
            for m in re.finditer(r"<h2[^>]*>(.*?)</h2>", content, flags=re.S)
        )

        prev_part = (
            f'<a href="{PAGES[i - 1][0]}.html"><span class="dir">Назад</span>{html.escape(PAGES[i - 1][1])}</a>'
            if i > 0 else "<div></div>"
        )
        next_part = (
            f'<a class="next" href="{PAGES[i + 1][0]}.html"><span class="dir">Дальше</span>{html.escape(PAGES[i + 1][1])}</a>'
            if i + 1 < len(PAGES) else ""
        )

        page = (
            f"{page_head}<body>\n<div class=\"layout\">\n<aside class=\"sidebar\">\n"
            f'  <span class="brand">Catppuccin Mocha</span>\n  <nav><div class="part-label">SwarmNav</div>\n{nav}</nav>\n</aside>\n'
            f'<main class="content">\n<div class="page-kicker">SwarmNav</div>\n{content}\n'
            f'<div class="pager">{prev_part}{next_part}</div>\n</main>\n'
            f'<aside class="onpage">\n  <div class="label">На этой странице</div>\n{toc}</aside>\n</div>\n</body>\n</html>\n'
        )
        with open(os.path.join(HERE, name + ".html"), "w", encoding="utf8") as f:
            f.write(page)
        print(f"{name}.html  ({title_text})")


if __name__ == "__main__":
    build()
