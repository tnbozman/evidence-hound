#!/usr/bin/env bash
# Seeds a disposable CISO Assistant for Evidence Hound with a service account, its token and the example
# staff users. Runs INSIDE the backend container (scripts/ca-seed.sh, the dev container's ca-seed service
# and the functional test fixture call it), then `hound scope apply` writes the example scope from outside.
# Safe to run more than once. The last line of output is HOUND_CA_TOKEN=<personal access token>.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd /code

python manage.py shell < "$here/seed.py"
