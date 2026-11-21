# Audit update plan

`hound plan` turns one run's results into the exact list of changes to send to a CISO Assistant audit. Publishing (s02) is then a plain loop over that list.

```console
$ hound plan --dry-run --config examples/example-pty-ltd.yaml --checks tests/fixtures/checks-sample.json
```

## Inputs

- **Config `target`** (see [`examples/example-pty-ltd.yaml`](../examples/example-pty-ltd.yaml)): the mapping file, the audit's `ref_id` (`compliance_assessment`) and a map from control keys to applied control `ref_id`s. IDs always come from config, never from code.
- **Check results file**: `checks` (as for `hound mapping apply`), plus optional `evidence` (`id`, `file_name`, `media_type`, `sha256`, `size_bytes`, `collected_at`) and `control_statuses` (`control` key and `status`: `to_do`, `in_progress`, `on_hold`, `active`, `deprecated`).

## Output

A JSON array of [CISO Assistant dispatcher](https://github.com/intuitem/ciso-assistant-community/tree/main/dispatcher) messages, in this order:

1. `upload_attachment` for each evidence item, by ID. Selector `{name: file_name, ref_id: id}`; values `{file_content, file_name}`. In a dry run `file_content` describes the file instead of holding its base64 content.
2. `update_requirement_assessment` for each requirement result, in mapping order. Selector `{compliance_assessment__ref_id, requirement__ref_id}`; values `{result, observation, evidences}`, where `evidences` lists the `ref_id`s of evidence uploaded in step 1.
3. `update_applied_control` for each control status, by key. Selector `{ref_id}`; values `{status}`.

The plan fails, with every problem listed and exit code 3, if a requirement cites evidence that is not in the file or a control key is not in the target.
