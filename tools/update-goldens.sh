#!/usr/bin/env bash
# Accepts a CI run's scenario captures as the new golden images.
#
#   tools/update-goldens.sh <run-id>      # e.g. from the failing PR's "Scenarios" job
#
# Goldens come from CI, not a local machine, because CI's software renderer is the reference;
# a Mac's GPU draws anti-aliased edges slightly differently. Commit the result: the PR then shows
# the image diff, which is how a reviewer sees exactly what changed.
set -euo pipefail

run_id="${1:?usage: tools/update-goldens.sh <run-id>}"
repo_root="$(git rev-parse --show-toplevel)"
download="$(mktemp -d)"
trap 'rm -rf "$download"' EXIT

gh run download "$run_id" --name scenarios --dir "$download"
mkdir -p "$repo_root/tests/golden"

count=0
for capture in "$download"/*.png; do
  case "$capture" in
    *.golden.png | *.diff.png) continue ;;
  esac
  cp "$capture" "$repo_root/tests/golden/"
  count=$((count + 1))
done

echo "updated $count golden images in tests/golden/ from run $run_id"
git -C "$repo_root" status --short tests/golden
