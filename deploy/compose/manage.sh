#!/usr/bin/env bash
# Runs a CISO Assistant management command in the backend container, with its secrets (through ca-env.sh).
# Run from deploy/compose with the stack up; docker compose's own settings (.env, COMPOSE_FILE, COMPOSE_PROJECT_NAME) apply.
#
# Usage: deploy/compose/manage.sh [-e NAME=VALUE ...] <command> [args]    e.g. ./manage.sh createsuperuser
#        deploy/compose/manage.sh shell <script.py                        (standard input is passed through)
set -euo pipefail

options=()
while [[ $# -ge 2 && "$1" == "-e" ]]; do
  options+=(-e "$2")
  shift 2
done
[[ $# -ge 1 ]] || { echo "usage: manage.sh [-e NAME=VALUE ...] <command> [args]" >&2; exit 2; }
# Interactive commands such as createsuperuser need a terminal; piped ones (shell <script.py) must not get one.
[[ -t 0 && -t 1 ]] || options+=(-T)

exec docker compose exec "${options[@]}" backend /bin/sh /hound/ca-env.sh python manage.py "$@"
