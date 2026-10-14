#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-only
# Run the portable local build with a separate development settings profile.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP="${ROOT}/dist/fl-editors/Nota.App"
if [[ ! -x "${APP}" || ! -f "${ROOT}/dist/fl-editors/libnota_engine.so" ]]; then
    echo 'Build the native engine and publish dist/fl-editors first; see docs/fl-editors.md.' >&2
    exit 1
fi
export NOTA_DATA_DIR="${NOTA_DATA_DIR:-${XDG_CONFIG_HOME:-${HOME}/.config}/nota-fl-editors}"
exec "${APP}" "$@"
