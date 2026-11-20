#!/usr/bin/env bash
# Pure helpers for sprint files. Source this file; every function reads its
# arguments and prints to stdout, with no git or network side effects.

SPRINTS_DIR="${SPRINTS_DIR:-docs/sprints}"

# sprint_valid_id sNN -> exit 0 if the id looks like s00..s99
sprint_valid_id() {
  [[ "${1:-}" =~ ^s[0-9]{2}$ ]]
}

# sprint_file sNN -> path of the single docs/sprints/sNN-*.md file
sprint_file() {
  local id="$1" matches
  matches=$(find "$SPRINTS_DIR" -maxdepth 1 -name "${id}-*.md" | sort)
  [[ -n "$matches" ]] || return 1
  [[ $(printf '%s\n' "$matches" | wc -l) -eq 1 ]] || return 2
  printf '%s\n' "$matches"
}

# sprint_slug path -> sNN-some-slug
sprint_slug() {
  basename "$1" .md
}

# sprint_title path -> title text after "# sNN "
sprint_title() {
  sed -n '1s/^# s[0-9][0-9] //p' "$1"
}

# sprint_status path -> Planned | In progress | Done ...
sprint_status() {
  sed -n 's/^Status: //p' "$1" | head -n1
}

# sprint_tasks path -> one "- [ ] sNN-TT Title" line per task heading
sprint_tasks() {
  sed -n 's/^### \(s[0-9][0-9]-[0-9][0-9]\) \(.*\)$/- [ ] \1 \2/p' "$1"
}

# sprint_pr_body path -> Markdown body for the sprint PR
# shellcheck disable=SC2016  # backticks are literal Markdown
sprint_pr_body() {
  local file="$1"
  printf 'Sprint plan: `%s`\n\n' "$file"
  printf '## Tasks\n\n'
  sprint_tasks "$file"
  printf '\n## Definition of done\n\n'
  printf -- '- [ ] Every task ticked, with its functional check recorded in the sprint file\n'
  printf -- '- [ ] CI, CodeQL, secret scanning and dependency review green\n'
  printf -- '- [ ] Sprint review note added and `Status: Done` set\n'
}

# sprint_set_status path status -> rewrites the Status line in place
sprint_set_status() {
  sed -i "s/^Status: .*/Status: $2/" "$1"
}
