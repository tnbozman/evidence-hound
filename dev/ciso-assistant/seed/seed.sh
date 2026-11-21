#!/usr/bin/env bash
# Seeds a disposable CISO Assistant for Evidence Hound. Runs INSIDE the backend container
# (scripts/ca-seed.sh and the functional test fixture call it). Safe to run more than once.
# The last line of output is HOUND_CA_TOKEN=<personal access token>.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd /code

python manage.py storelibraries --path "$here/example-baseline.yaml" >/dev/null
python manage.py shell < "$here/seed.py"
