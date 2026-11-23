---
name: read-an-api-specification
description: Read a system's API specification to find which operations can evidence a requirement. Use when deciding api_check requirements or writing a profile.
includes: [reading-an-api-specification]
---
## Read an API specification

- Give the specification to `hound` with `--spec` on `hound onboard <system> pending --json`; its answer summarises the operations. Read the specification file itself for the detail of a response.
- Propose an `api_check` only for an operation that is in the specification and only reads. Name it as `METHOD /path` in `--source`.
- Never call the system's API yourself and never ask for its credentials. `hound` samples it later, with the person's go-ahead.
