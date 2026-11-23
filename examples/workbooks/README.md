# Example audit workbook

`example-audit-workbook.csv` is a made-up audit workbook for Example Pty Ltd: the kind of spreadsheet an organisation keeps before it uses
Evidence Hound. It is here to try `hound scope import` against the example compliance repo:

```console
$ hound scope import examples/workbooks/example-audit-workbook.csv --repo <a copy of examples/compliance-repo> --audit EX-E8-2026
```

Two rows name who approved them and are imported as approved; one has no approver and waits for `hound approve`; one is not assessed and is
left out; one is not a requirement of the framework and is listed as unmatched. See [onboarding](../../docs/onboarding.md).
