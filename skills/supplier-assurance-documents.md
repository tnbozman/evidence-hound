---
name: supplier-assurance-documents
description: Which supplier documents count as assurance, and which do not.
version: 1.0.0
applies_to: [supplier]
---
# Supplier assurance documents

Judge a document by who checked what, when, and against which standard.

**Counts as assurance, if current and in scope:**
- An independent audit report or certificate against a recognised standard (for example ISO/IEC 27001, SOC 2 Type II, IRAP), with the certificate's scope covering the service used and a date within the last year (or its stated validity).
- A SOC 2 Type II report's exceptions and complementary user entity controls: note any the organisation must operate itself.
- A completed security questionnaire signed by the supplier, for a supplier too small for certification. Weigh it lower than an independent audit.
- Penetration test summaries from an independent tester, with findings and their remediation.

**Does not count on its own:**
- Marketing pages, trust-centre summaries and "compliant with" badges without the report behind them.
- A SOC 2 Type I report (design only, not operation over time), or any report more than a year old.
- A certificate whose scope does not include the service, region or entity the organisation uses.
- The supplier's own policies, unless an independent party has checked they operate.

When a document is missing or out of scope, say what to ask the supplier for. Never treat the absence of findings in an unseen document as assurance.
