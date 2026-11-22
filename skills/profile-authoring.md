---
name: profile-authoring
description: Writing a REST profile that hound can test and run.
version: 1.0.0
applies_to: [profile]
---
# Writing a REST profile

A profile is YAML that the generic REST collector runs. Follow docs/collectors/rest.md exactly; never invent keys.

- **Parameters and secrets.** Anything an organisation sets (client ID, supported versions, limits) is a parameter with a description; credentials are secrets, never parameters, never defaults.
- **Connection.** Set `base_url`, list every host that may receive the credential in `allowed_hosts`, and use the authentication type the API documents.
- **Bootstrap only what queries need.** Use `bootstrap` for values such as a tenant ID or region that later paths need. It is not evidence.
- **Queries read everything.** Use the paging style the API documents so every page is read. Set `items` to the list in the response.
- **Identities.** List in `identities` every field that names a person or device: host names, serial numbers, user names, emails, IP and MAC addresses. They are hashed before evidence is saved.
- **Checks.** One check per requirement, with an `id` that says what it tests. Use the operator that matches the data (`version_in` for versions, `in` for allowed values, `count_at_most` for limits). Set `when_empty` deliberately: `not_applicable` only when an empty list really means nothing to assess, otherwise `fail`.
- **Missing fields.** Decide with `missing:` whether a missing field passes or fails; a missing security value usually fails.
- **Describe for people.** `describe` says in one sentence what was found, with counts.
- **Test first.** Every profile ships with test cases: one where the check passes and one where it fails, from recorded responses.

Draft only from the specification and response shapes given; mark anything you are unsure of for the person to confirm.
