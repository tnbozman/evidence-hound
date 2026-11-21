# Migrating from the Essential Eight to the ASD Essentials

ASD's **Essentials for enterprise IT** replaces the **Essential Eight Maturity Model (November 2023)**. This plan covers how an Evidence Hound deployment moves its assessment from the Essential Eight to the Essentials without losing evidence or history. It also covers what Evidence Hound must change to support the Essentials.

The figures below come from the libraries that ship with CISO Assistant v4.3.4, the version in `dev/ciso-assistant`:

| | Library URN | Framework URN |
| --- | --- | --- |
| From | `urn:intuitem:risk:library:essential-eight-2023` | `urn:intuitem:risk:framework:essential-eight-2023` |
| To | `urn:intuitem:risk:library:asd-essentials-enterprise-it` | `urn:intuitem:risk:framework:asd-essentials-enterprise-it` |
| Transition mapping | `urn:intuitem:risk:library:mapping-essential-eight-2023-and-asd-essentials-enterprise-it` | Essential Eight 2023 to Essentials |

Do not use `urn:intuitem:risk:library:essential-eight`. CISO Assistant now marks it "legacy", and the 2023 library replaces it.

Always refer to libraries and frameworks by URN. The `/stored-libraries/<uuid>` address in the CISO Assistant UI is different on every instance.

## Timeline

| When | Frameworks in use | Evidence Hound needs |
| --- | --- | --- |
| Now | Essential Eight 2023 at ML2 is the audit of record | Requirement rows by URN and the ML2 mapping (s06) |
| In a few months | Essential Eight and the Essentials in parallel, then the Essentials alone | Several audits per run (s06) and the Essentials mapping (s11) |
| A few months later | The Essentials and NIST SP 800-171 in parallel | The 800-171 mapping, SSP and POA&M (s13) |

The parallel run in stage 1 below uses the same mechanism as running the Essentials and 800-171 together later: one collection run, one evidence upload, several audits.

## Summary

- **The requirements can be mapped, but only partly.** ASD's mapping links 81 of the 87 Essential Eight ISM controls in scope at ML1 and ML2 to 32 of the 58 Essentials actions (35 counting ML3 controls). Every link is `intersect`, meaning the two requirements overlap but neither fully covers the other.
- **CISO Assistant cannot carry a "compliant" result across.** For an `intersect` link it turns `compliant` into `partially_compliant` at most (`RequirementAssessment.infer_result` in v4.3.4). A mapped audit is a starting point to review, not an answer.
- **Evidence Hound can do better.** Its checks are framework-neutral, so it can map the same checks straight onto Essentials actions with their own mapping file. Results then come from fresh evidence, with no cap.
- **About 40 % of the Essentials is new ground.** 23 actions have no Essential Eight source, mostly governance, supplier assurance, training, content filtering and segmentation. They need new checks, or manual sources with attestations.
- **Evidence Hound has to change before either framework works.** Both libraries reuse a requirement's `ref_id` in more than one place, but Evidence Hound selects requirement rows by `ref_id` alone. See [Changes to Evidence Hound](#changes-to-evidence-hound).

## How the frameworks differ

| | Essential Eight (Nov 2023) | ASD Essentials for enterprise IT |
| --- | --- | --- |
| Structure | 8 mitigation strategies, each with ISM controls | 8 domains, then 28 principles, then 58 actions |
| Assessable rows | 152 (126 distinct ISM `ref_id`s) | 212 (58 distinct action `ref_id`s, each rated in every domain it applies to) |
| Scope selector (implementation groups) | Maturity Level 1, 2 or 3 | Priority phase 1, 2 or 3 |
| What a group means | How capable an adversary the controls resist | Which parts of the environment are assessed first |
| Rating | Each control rated per strategy | Each action rated per domain; a principle takes its **lowest** applicable action in that domain (weakest link) |

The Essentials domains and their phases:

| Domain | Priority phase | Assessable rows | Rows with an Essential Eight source |
| --- | --- | --- | --- |
| Governance and culture | 1, 2, 3 | 9 | 1 |
| Visibility and response | 1, 2, 3 | 12 | 8 |
| Boundary services | 1 | 37 | 26 |
| Identity services | 1 | 35 | 26 |
| Critical processes and services | 2 | 37 | 26 |
| Privileged environments | 2 | 35 | 26 |
| Business systems | 3 | 27 | 20 |
| User devices | 3 | 20 | 15 |

"Governance and culture" and "Visibility and response" are the capability domains, and they are assessed in every phase.

The Essentials' phases are not a new name for maturity levels. ML2 asked how strong the controls are everywhere. Phase 1 asks whether boundary and identity services are fully effective. A stable ML2 programme therefore maps to **phase 1 first**, then phase 2, rather than to "phases 1 and 2" in one step.

## The requirement mapping

ASD's mapping, packaged by intuitem, has 975 links. Each one goes from an Essential Eight row to an Essentials row, for every domain the action applies to. All of them are `intersect` with `functional` rationale.

### Essential Eight controls with no Essentials target

Six ISM controls in scope at ML1 and ML2 are not mapped. They are not dropped by ASD as unimportant; the Essentials covers them under broader actions with no direct link. Assess them by judgement during the parallel run, and record where they now fit.

| ISM | Strategy | Maturity levels | Requirement |
| --- | --- | --- | --- |
| ISM-0123 | Application control, Multi-factor authentication, Restrict administrative privileges, User application hardening | 2, 3 | Cyber security incidents are reported to the chief information security officer, or one of their delegates, as soon as possible… |
| ISM-1687 | Restrict administrative privileges | 2, 3 | Privileged operating environments are not virtualised within unprivileged operating environments. |
| ISM-1704 | Patch applications | 1, 2, 3 | Office productivity suites, web browsers and their extensions, email clients, PDF software, Adobe Flash Player, and security… |
| ISM-1705 | Regular backups | 2, 3 | Privileged user accounts (excluding backup administrator accounts) cannot access backups belonging to other user accounts. |
| ISM-1707 | Regular backups | 2, 3 | Privileged user accounts (excluding backup administrator accounts) are prevented from modifying and deleting backups. |
| ISM-1905 | Patch applications | 1, 2, 3 | Online services that are no longer supported by vendors are removed. |

Suggested homes, to confirm with ASD's guidance: ISM-0123 under RES-03 (incident response), ISM-1687 under PRO-05 (secure administration), ISM-1704 and ISM-1905 under GOV-14 (legacy systems) and PRO-06 (vulnerability management), and ISM-1705 and ISM-1707 under PRO-10.2 (backup isolation).

### Essentials actions with an Essential Eight source

"Domains" is how many domains rate the action. "Sources" counts the ISM controls mapped to it at any maturity level; only those in scope at ML1 and ML2 are listed. Three actions (IT.PRO-01.1, IT.PRO-05.2 and IT.PRO-13.2) are mapped only from ML3 controls. An ML2 programme has no prior evidence for them.

| Action | Principle | Domains | Sources | ML1–ML2 sources |
| --- | --- | --- | --- | --- |
| IT.DET-01.1 | Centralised event logging | 6 | 1 (1 at ML1–ML2) | ISM-1815 |
| IT.DET-01.2 | Centralised event logging | 6 | 6 (6 at ML1–ML2) | ISM-1509, ISM-1623, ISM-1650, ISM-1660, ISM-1683, ISM-1889 |
| IT.DET-01.3 | Centralised event logging | 1 | 1 (1 at ML1–ML2) | ISM-1815 |
| IT.DET-02.2 | Cyber security event detection | 1 | 3 (2 at ML1–ML2) | ISM-1660, ISM-1683 |
| IT.DET-03.1 | Cyber security incident identification | 1 | 4 (2 at ML1–ML2) | ISM-1228, ISM-1906 |
| IT.DET-03.2 | Cyber security incident identification | 1 | 1 (1 at ML1–ML2) | ISM-1228 |
| IT.DET-04.1 | Baseline high-risk access activities | 4 | 4 (3 at ML1–ML2) | ISM-1228, ISM-1623, ISM-1660 |
| IT.DET-05.1 | Detection capability efficacy | 1 | 1 (1 at ML1–ML2) | ISM-1807 |
| IT.GOV-14.1 | Legacy system management | 1 | 1 (1 at ML1–ML2) | ISM-1501 |
| IT.IDE-01.1 | Asset identification | 1 | 1 (1 at ML1–ML2) | ISM-1807 |
| IT.PRO-01.1 | Secure system lifecycle | 6 | 1 (0 at ML1–ML2) | — |
| IT.PRO-04.1 | Secure configuration management | 6 | 27 (17 at ML1–ML2) | ISM-1412, ISM-1485, ISM-1486, ISM-1488, ISM-1542, ISM-1585, ISM-1654, ISM-1667, ISM-1668, ISM-1669, ISM-1670, ISM-1671, ISM-1673, ISM-1823, ISM-1824, ISM-1859, ISM-1860 |
| IT.PRO-04.2 | Secure configuration management | 6 | 6 (5 at ML1–ML2) | ISM-1412, ISM-1489, ISM-1672, ISM-1859, ISM-1860 |
| IT.PRO-04.3 | Secure configuration management | 6 | 1 (1 at ML1–ML2) | ISM-1582 |
| IT.PRO-05.1 | Secure administration | 4 | 5 (4 at ML1–ML2) | ISM-1380, ISM-1387, ISM-1688, ISM-1689 |
| IT.PRO-05.2 | Secure administration | 4 | 1 (0 at ML1–ML2) | — |
| IT.PRO-05.3 | Secure administration | 4 | 2 (2 at ML1–ML2) | ISM-1509, ISM-1650 |
| IT.PRO-06.1 | Vulnerability management | 6 | 9 (7 at ML1–ML2) | ISM-1698, ISM-1699, ISM-1700, ISM-1701, ISM-1702, ISM-1807, ISM-1808 |
| IT.PRO-06.2 | Vulnerability management | 6 | 15 (7 at ML1–ML2) | ISM-1690, ISM-1691, ISM-1693, ISM-1694, ISM-1695, ISM-1876, ISM-1877 |
| IT.PRO-06.3 | Vulnerability management | 6 | 15 (7 at ML1–ML2) | ISM-1690, ISM-1691, ISM-1693, ISM-1694, ISM-1695, ISM-1876, ISM-1877 |
| IT.PRO-07.1 | Trustworthy software | 6 | 18 (10 at ML1–ML2) | ISM-0843, ISM-1488, ISM-1490, ISM-1544, ISM-1582, ISM-1657, ISM-1672, ISM-1673, ISM-1870, ISM-1871 |
| IT.PRO-07.2 | Trustworthy software | 6 | 2 (1 at ML1–ML2) | ISM-1657 |
| IT.PRO-10.1 | Regular and proven backups | 5 | 3 (3 at ML1–ML2) | ISM-1511, ISM-1810, ISM-1811 |
| IT.PRO-10.2 | Regular and proven backups | 5 | 6 (3 at ML1–ML2) | ISM-1811, ISM-1812, ISM-1814 |
| IT.PRO-10.3 | Regular and proven backups | 5 | 2 (2 at ML1–ML2) | ISM-1511, ISM-1515 |
| IT.PRO-12.1 | Least privilege access | 6 | 3 (2 at ML1–ML2) | ISM-1507, ISM-1647 |
| IT.PRO-12.2 | Least privilege access | 4 | 3 (2 at ML1–ML2) | ISM-1175, ISM-1507 |
| IT.PRO-12.3 | Least privilege access | 4 | 4 (3 at ML1–ML2) | ISM-0445, ISM-1175, ISM-1883 |
| IT.PRO-13.1 | Identity, credential and access management | 6 | 2 (2 at ML1–ML2) | ISM-1647, ISM-1648 |
| IT.PRO-13.2 | Identity, credential and access management | 5 | 2 (0 at ML1–ML2) | — |
| IT.PRO-13.4 | Identity, credential and access management | 6 | 15 (12 at ML1–ML2) | ISM-0974, ISM-1173, ISM-1401, ISM-1504, ISM-1679, ISM-1680, ISM-1681, ISM-1682, ISM-1872, ISM-1873, ISM-1892, ISM-1893 |
| IT.PRO-13.6 | Identity, credential and access management | 6 | 1 (1 at ML1–ML2) | ISM-1685 |
| IT.PRO-18.2 | Network segmentation and segregation | 5 | 1 (1 at ML1–ML2) | ISM-1380 |
| IT.RES-03.1 | Cyber security incident response | 1 | 1 (1 at ML1–ML2) | ISM-1819 |
| IT.RES-05.1 | Cyber security incident coordination | 1 | 1 (1 at ML1–ML2) | ISM-0140 |

### Essentials actions with no Essential Eight source

These are new work. Most are processes rather than technical settings, so a manual source (see [configuration.md](../configuration.md#manual-sources)) with an owner and a due interval is the natural first step. Content filtering (PRO-09), legacy authentication (PRO-13.5) and secret scanning (PRO-13.3) are good candidates for automated collectors later.

| Action | Principle | Summary |
| --- | --- | --- |
| IT.DET-02.1 | Cyber security event detection | Ensure event logs are normalised, time-synchronised and enriched to support correlation across systems and… |
| IT.GOV-01.1 | Executive cyber security accountability | Formally define and establish executive and board-level accountabilities for cyber security, including… |
| IT.GOV-03.1 | Security risk management assurance | Establish and maintain an approved risk management methodology and plan that is used to assess and treat… |
| IT.GOV-04.1 | Cyber security resourcing | Assign sufficient cyber security personnel and resources with the required skills to operate, monitor,… |
| IT.GOV-04.2 | Cyber security resourcing | Periodically review cyber security resourcing against cyber threat trends, cyber security incident outcomes… |
| IT.GOV-08.1 | Executive artificial intelligence accountability | Establish and maintain executive accountability for the use, governance and risk management of artificial… |
| IT.GOV-11.1 | Supplier cyber security assurance | Select and engage suppliers of software, hardware and services on the basis of a cyber supply chain risk… |
| IT.GOV-11.2 | Supplier cyber security assurance | Maintain current assurance that suppliers continue to meet their cyber security commitments, and act when… |
| IT.IDE-01.2 | Asset identification | Maintain current records of infrastructure, operating systems, applications, identities, credentials and data… |
| IT.IDE-04.1 | Security risk identification | Identify relevant weaknesses, malicious actor techniques and cyber threat behaviours when undertaking risk… |
| IT.PRO-01.2 | Secure system lifecycle | Secure build and deployment processes to enforce controls consistently across systems. |
| IT.PRO-09.1 | Content filtering | Inspect outbound content flows to detect unauthorised transfer of sensitive or high-value data. |
| IT.PRO-09.2 | Content filtering | Block outbound network connections to known malicious or untrusted destinations. |
| IT.PRO-09.3 | Content filtering | Inspect inbound content from external or untrusted sources to prevent delivery of malicious or unsafe content. |
| IT.PRO-13.3 | Identity, credential and access management | Identify and remediate unsecured keys, secrets and credentials by regularly scanning codebases, configuration… |
| IT.PRO-13.5 | Identity, credential and access management | Disable legacy authentication protocols or, where this is not feasible, restrict and monitor their use, apply… |
| IT.PRO-14.1 | Cyber security awareness training | Ensure all personnel receive cyber security awareness training, that is tailored to how they will interact… |
| IT.PRO-14.2 | Cyber security awareness training | Update and provide cyber security awareness training on a regular basis to reflect current threats and… |
| IT.PRO-16.1 | Cyber supply chain security | Assess the integrity and authenticity of software, components and updates before use, and verify provenance… |
| IT.PRO-16.2 | Cyber supply chain security | Use vendor advisories and threat intelligence to maintain awareness of supplier, product and component… |
| IT.PRO-18.1 | Network segmentation and segregation | Isolate systems within network zones based on business criticality and trust level, and restrict and monitor… |
| IT.RES-04.1 | Cyber security incident insights | Perform post-cyber security incident reviews to identify the security gaps that enabled each stage of the… |
| IT.RES-04.2 | Cyber security incident insights | Evaluate the monitoring coverage and cyber security incident response procedures following cyber security… |

## Changes to Evidence Hound

These are the changes Evidence Hound needs before it can publish to the Essentials. Changes 1, 2, 3, 5 and 6 are planned in [s06](../sprints/s06-ciso-assistant-as-configuration.md), and change 4 in [s11](../sprints/s11-essentials-mapping-and-adoption.md).

1. **Address requirement rows by URN.** Today `hound publish` selects a requirement assessment by audit `ref_id` and requirement `ref_id` (`Selector.RequirementInAudit`). In the Essentials, 37 of 58 action `ref_id`s appear in more than one domain. In the Essential Eight, 10 ISM `ref_id`s appear under more than one strategy, ISM-0123 under four. A mapping file must be able to name a requirement by its URN, such as `urn:intuitem:risk:req_node:asd-essentials-enterprise-it:identity-services:pro-13:it.pro-13.4`. For readability it could also use a domain-qualified form, such as `identity-services/IT.PRO-13.4`. This also blocks the Essential Eight 2023 mapping kept for the transition.
2. **Scope the mapping to the audit's groups.** A mapping file covers the whole framework. The publisher should skip requirements outside the audit's selected implementation groups (maturity levels or phases), rather than writing results CISO Assistant hides.
3. **Fix the dev seed.** `dev/ciso-assistant/seed/seed.py` imports the legacy `essential-eight` library. It should import `essential-eight-2023`, `asd-essentials-enterprise-it` and the transition mapping.
4. **Generate a draft mapping.** Add a script or `hound mapping derive` command that reads an Essential Eight mapping file and the transition mapping, then writes a draft Essentials mapping file. Each action row gets the union of the checks of its source ISM controls, with `rule: all_pass`, and is marked as a draft for review. The draft is never published until a person has reviewed it, because `intersect` means the checks may not cover the whole action.
5. **Publish to more than one audit.** The config has one `target`. The parallel run needs two: the Essential Eight audit with its mapping, and the Essentials audit with its own.
6. **Systems know their Essentials domain.** Essentials actions are rated per domain, so the scope file (see [s06](../sprints/s06-ciso-assistant-as-configuration.md)) should let each system say which Essentials domain it belongs to, such as boundary services or user devices. The mapping then rolls up only the evidence from systems in that domain.

## Migration plan

### Stage 0: prepare (before any audit changes)

- Load `essential-eight-2023`, `asd-essentials-enterprise-it` and the transition mapping into CISO Assistant. Check that their URNs match the table at the top of this page.
- If your current Essential Eight audit uses the legacy library, move it to `essential-eight-2023` first. CISO Assistant ships `mapping-essential-eight-and-essential-eight-2023` for that step.
- Ship Evidence Hound changes 1 to 3 and 5 above.

**Exit:** `hound mapping validate` passes for the Essential Eight mapping using URNs, and the current Essential Eight audit publishes as before.

### Stage 1: parallel run

- Keep the Essential Eight audit as the audit of record. Keep publishing to it.
- Create an Essentials audit in the same perimeter with priority phase 1 selected. Use CISO Assistant's "create from another audit" with the transition mapping, to start every mapped row with a suggested `partially_compliant` or `non_compliant`.
- Generate the draft Essentials mapping (change 4). Review every action row with the system owner and the assessor. Keep a check only if it evidences the action, add checks where the action asks for more, and record the reason in the review.
- Add a second `target` for the Essentials audit (change 5), so one collection run publishes to both audits.

**Exit:** every phase 1 row in the Essentials audit has a result from Evidence Hound or a named manual source. No row's result depends only on mapping inference.

### Stage 2: close the gaps

- For each action with no Essential Eight source, either add a manual source with an owner and due interval, or plan a collector. Supplier assurance (GOV-11), supply chain security (PRO-16) and risk management (GOV-03) can be evidenced from CISO Assistant's own records by the governance collector ([s10-03](../sprints/s10-third-parties-and-metrics.md)), once suppliers and risk studies are managed as configuration.
- Place the six unmapped ISM controls under their Essentials actions, and record the decision in the mapping file's comments.
- Tag each system with its Essentials domain (change 6), and check the per-domain roll-up matches what an assessor would conclude.

**Exit:** phase 1 has no `not_assessed` rows, and the committee has accepted the Essentials results alongside the Essential Eight results for one reporting period.

### Stage 3: cut over

- Make the Essentials audit the audit of record. Report against it to the committee and board.
- Stop publishing to the Essential Eight audit, then lock it in CISO Assistant. It stays as the historical record, with its evidence.
- Add phase 2, then phase 3, as their own exit criteria are met.

**Exit:** one reporting cycle runs on the Essentials alone.

### Rollback

Until stage 3, the Essential Eight audit is untouched and still published to, so rolling back means removing the Essentials `target` from the config. After stage 3, unlock the Essential Eight audit and add its `target` back. Evidence collected meanwhile is still in the evidence store, and the next run publishes to both audits.

## To verify

- How CISO Assistant combines several source rows that map to one target row when it creates an audit from a mapping. Up to 28 Essential Eight rows map to one Essentials action.
- Whether "create from another audit" also copies evidence and applied controls, or only results.
- Whether the REST API can import the transition mapping library on an instance where it is not already stored.

## Sources

- ASD, [Essentials for enterprise IT](https://www.cyber.gov.au/essentials/essentials-for-enterprise-it). © Commonwealth of Australia 2026, CC BY 4.0, attributed to the Australian Signals Directorate.
- CISO Assistant community libraries at tag v4.3.4: `essential-eight-2023.yaml`, `asd-essentials-enterprise-it.yaml` and `mapping-essential-eight-2023-and-asd-essentials-enterprise-it.yaml` in `backend/library/libraries/`.
- Mapping inference: `RequirementMapping.coverage` and `RequirementAssessment.infer_result` in `backend/core/models.py` at v4.3.4.
