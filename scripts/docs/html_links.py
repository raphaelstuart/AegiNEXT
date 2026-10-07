import re
from html import escape, unescape
from html.parser import HTMLParser


URL_ATTRIBUTE = re.compile(
    r'''(?<![\w:-])(?P<name>href|src)\s*=\s*(?:"(?P<double>[^"]*)"|'(?P<single>[^']*)'|(?P<bare>[^\s>]+))''',
    re.IGNORECASE,
)


class HtmlLinkRewriter(HTMLParser):
    def __init__(self, content, resolve):
        super().__init__(convert_charrefs=False)
        self.resolve = resolve
        self.replacements = []
        self.line_offsets = [0]
        for line in content.splitlines(keepends=True):
            self.line_offsets.append(self.line_offsets[-1] + len(line))
        self.feed(content)
        self.close()

    def handle_starttag(self, tag, attrs):
        if tag not in ("a", "img"):
            return
        line, column = self.getpos()
        offset = self.line_offsets[line - 1] + column
        for match in URL_ATTRIBUTE.finditer(self.get_starttag_text()):
            group = next(name for name in ("double", "single", "bare") if match.group(name) is not None)
            url = unescape(match.group(group))
            resolved = self.resolve(url)
            if resolved != url:
                replacement = f'{match.group("name")}="{escape(resolved, quote=True)}"'
                self.replacements.append((offset + match.start(), offset + match.end(), replacement))

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)


def rewrite_html(content, resolve):
    parser = HtmlLinkRewriter(content, resolve)
    for start, end, replacement in reversed(parser.replacements):
        content = content[:start] + replacement + content[end:]
    return content
