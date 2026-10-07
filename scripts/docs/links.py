import posixpath
from pathlib import Path
from urllib.parse import quote, unquote, urlsplit, urlunsplit


def resolve_link(url, config, files, page_file, *, rendered=False):
    parts = urlsplit(url)
    if parts.scheme or parts.netloc or not parts.path or parts.path.startswith("/"):
        return url

    path = posixpath.normpath(posixpath.join(posixpath.dirname(page_file.src_uri), unquote(parts.path)))
    target = files.get_file_from_path(path)
    if target is not None:
        if not rendered:
            return url
        return urlunsplit(("", "", target.url_relative_to(page_file), parts.query, parts.fragment))

    root = Path(config.config_file_path).resolve().parent
    source = (root / path).resolve()
    if not source.is_relative_to(root) or not source.exists():
        raise ValueError(f"{page_file.src_uri}: missing repository link target {url!r}")

    kind = "tree" if source.is_dir() else "blob"
    branch = quote(config.extra["repository_branch"], safe="")
    repository_url = f"{config.repo_url.rstrip('/')}/{kind}/{branch}/{quote(path, safe='/')}"
    target_parts = urlsplit(repository_url)
    return urlunsplit((*target_parts[:3], parts.query, parts.fragment))
