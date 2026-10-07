from markdown.extensions import Extension

from scripts.docs.link_processor import RepositoryLinkProcessor


class RepositoryLinksExtension(Extension):
    def __init__(self, config, files, page_file):
        super().__init__()
        self.site_config = config
        self.files = files
        self.page_file = page_file

    def extendMarkdown(self, md):
        processor = RepositoryLinkProcessor(md, self.site_config, self.files, self.page_file)
        md.treeprocessors.register(processor, "aeginext_repository_links", 15)
