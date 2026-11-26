#!/usr/bin/env bats
# scripts/code-measures.sh: the s19-03 measures of the code, from what git tracks, so a review's before and after can be compared.

setup() {
  root="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
  repo="$BATS_TEST_TMPDIR/repo"
  mkdir -p "$repo/src/Example.Core" "$repo/tests/Example.Core.Tests" "$repo/src/Example.Core/obj"
  printf 'a\nb\nc\n' > "$repo/src/Example.Core/Big.cs"
  printf 'a\n' > "$repo/src/Example.Core/Small.cs"
  printf 'x\ny\n' > "$repo/tests/Example.Core.Tests/BigTests.cs"
  printf 'ignored\n' > "$repo/src/Example.Core/obj/Generated.cs"
  git -C "$repo" init -q
  git -C "$repo" add src/Example.Core/Big.cs src/Example.Core/Small.cs tests/Example.Core.Tests/BigTests.cs
}

@test "counts tracked C# lines per project and lists the largest files" {
  run "$root/scripts/code-measures.sh" "$repo"
  [ "$status" -eq 0 ]
  [[ "$output" == *"src/Example.Core 4 lines in 2 files"* ]]
  [[ "$output" == *"tests/Example.Core.Tests 2 lines in 1 files"* ]]
  [[ "$output" == *"Production 4, tests 2"* ]]
  [[ "$output" == *"3 src/Example.Core/Big.cs"* ]]
  [[ "$output" != *"Generated"* ]]
}
