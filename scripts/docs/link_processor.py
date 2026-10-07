from functools import partial

from markdown.treeprocessors import Treeprocessor

from scripts.docs.html_links import rewrite_html
from scripts.docs.links import resolve_link


class RepositoryLinkProcessor(Treeprocessor):
    def __init__(self, md, config, files, page_file):
        super().__init__(md)
        self.resolve = partial(resolve_link, config=config, files=files, page_file=page_file)

    def run(self, root):
        for element in root.iter():
            attribute = "href" if element.tag == "a" else "src" if element.tag == "img" else None
            if attribute and element.get(attribute):
                element.set(attribute, self.resolve(element.get(attribute)))
        for index, content in enumerate(self.md.htmlStash.rawHtmlBlocks):
            self.md.htmlStash.rawHtmlBlocks[index] = rewrite_html(
                content, partial(self.resolve, rendered=True)
            )
