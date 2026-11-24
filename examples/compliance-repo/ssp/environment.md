One office with a managed firewall, and cloud services. Staff work from the office and from home on company laptops only; there are no servers on site.

The boundary of the plan is the perimeter "Corporate IT". Everything inside the box below is in scope; the suppliers' platforms outside it are covered by their own assurance, recorded as third parties in CISO Assistant.

```mermaid
flowchart LR
    subgraph boundary["Corporate IT (plan boundary)"]
        laptops["Example laptops<br/>Windows, managed by Intune"]
        m365["Example 365<br/>mail, files, identity"]
        backup["Example backup service<br/>daily backups"]
    end
    staff(["Staff"]) --> laptops
    laptops -- "MFA sign-in" --> m365
    backup -- "reads mail and files" --> m365
    backup -- "backs up" --> laptops
    m365 -. "hosted by" .-> cloud[("Example Cloud Pty Ltd<br/>(supplier)")]
```

This diagram is fictional and drawn by hand: hound does not generate boundary or data flow diagrams.
