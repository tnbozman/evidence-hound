#!/usr/bin/env bash
# Live self-test for the org-data check: with the real ORG_DENYLIST plus a
# random canary term, a throwaway repository containing the canary must fail
# the check. Proves the deny-list secret is loaded and parsed, without ever
# printing or needing to know its terms.
#
# Usage: ORG_DENYLIST='...' scripts/org-data-self-test.sh
set -euo pipefail

check="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/check-org-data.sh"
[[ -n "${ORG_DENYLIST:-}" ]] || { echo "org-data-self-test: ORG_DENYLIST is empty" >&2; exit 2; }

canary="houndcanary$(od -An -N6 -tx1 /dev/urandom | tr -d ' \n')"
repo="$(mktemp -d)"
trap 'rm -rf "$repo"' EXIT

git -C "$repo" init -q -b main
git -C "$repo" -c user.name=self-test -c user.email=self-test@example.com \
  commit -q --allow-empty -m "base"
echo "Example Pty Ltd mentions $canary here." > "$repo/canary.txt"
git -C "$repo" add canary.txt
git -C "$repo" -c user.name=self-test -c user.email=self-test@example.com commit -q -m "add canary"

set +e
(cd "$repo" && ORG_DENYLIST="${ORG_DENYLIST}"$'\n'"${canary}" "$check" "HEAD~1..HEAD") >/dev/null 2>&1
status=$?
set -e

if [[ $status -ne 1 ]]; then
  echo "org-data-self-test: expected the check to fail on the canary (exit 1), got exit $status" >&2
  exit 1
fi
echo "org-data-self-test: check failed on the canary as expected"
