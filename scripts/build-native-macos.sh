#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
    printf '%s\n' 'The native HDR probe currently requires macOS. Use build.ps1 -Target Managed on other platforms.' >&2
    exit 2
fi

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_dir="$(cd -- "$script_dir/.." && pwd)"
if ! command -v pwsh >/dev/null 2>&1; then
    printf '%s\n' 'PowerShell is required: brew install powershell' >&2
    exit 2
fi

if [[ -n "${AEGINEXT_NATIVE_BUILD_DIR:-}" ]]; then
    printf '%s\n' 'AEGINEXT_NATIVE_BUILD_DIR is no longer used. build.ps1 isolates build directories by architecture and configuration.' >&2
    exit 2
fi

exec pwsh -NoLogo -NoProfile -File "$repo_dir/build.ps1" \
    -Target Native -Jobs "${AEGINEXT_NATIVE_BUILD_JOBS:-2}" "$@"
