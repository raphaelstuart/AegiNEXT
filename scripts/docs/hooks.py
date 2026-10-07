import os
from pathlib import Path
from urllib.parse import urljoin

from mkdocs.structure.files import File, Files

from scripts.docs.link_extension import RepositoryLinksExtension


def on_config(config):
    site_url = os.environ.get("AEGINEXT_DOCS_SITE_URL")
    if site_url:
        config.site_url = site_url.rstrip("/") + "/"
    config.extra["alternate"] = language_links(config)
    return config


def on_files(files, config):
    root = Path(config.config_file_path).resolve().parent
    docs = root / "docs"
    sources = {root / "README.md", root / "README.zh-CN.md"}
    sources.update(docs.rglob("*.md"))
    sources.update(path for path in (docs / "assets").rglob("*") if path.is_file())
    sources.update((docs / "en/examples/effects").rglob("*.aegifx"))
    sources.update([
        root / "src/AegiNext.Desktop/Assets/AppIcon.png",
        root / "src/AegiNext.Core/Effects/Scripts/fade-in-out.aegifx",
    ])
    generated = Files([])
    for source in sorted(sources):
        relative = source.relative_to(root)
        if any(part.startswith(".") for part in relative.parts):
            continue
        file = File.generated(config, relative.as_posix(), abs_src_path=str(source))
        file.edit_uri = relative.as_posix()
        generated.append(file)
    generated.add_files_from_theme(config.theme.get_env(), config)
    return generated


def language_links(config, page_file=None, files=None):
    english = "README.md"
    chinese = "README.zh-CN.md"
    if page_file and page_file.src_uri.startswith(("docs/en/", "docs/zh-cn/")):
        suffix = page_file.src_uri.split("/", 2)[2]
        english = f"docs/en/{suffix}"
        chinese = f"docs/zh-cn/{suffix}"
    return [
        {
            "name": name,
            "lang": language,
            "link": urljoin(config.site_url, (
                files.get_file_from_path(path) if files else File.generated(config, path, content="")
            ).url),
        }
        for name, language, path in (("English", "en", english), ("简体中文", "zh", chinese))
    ]


def on_page_markdown(markdown, page, config, files):
    config.markdown_extensions = [
        extension for extension in config.markdown_extensions
        if not isinstance(extension, RepositoryLinksExtension)
    ]
    config.markdown_extensions.append(RepositoryLinksExtension(config, files, page.file))
    page.meta["language"] = (
        "zh" if page.file.src_uri == "README.zh-CN.md" or page.file.src_uri.startswith("docs/zh-cn/") else "en"
    )
    page.meta["alternate"] = language_links(config, page.file, files)
    return markdown


def on_page_context(context, page, config, nav):
    context["config"] = dict(config, extra=dict(config.extra, alternate=page.meta["alternate"]))
    return context
