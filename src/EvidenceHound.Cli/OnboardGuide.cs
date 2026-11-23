namespace EvidenceHound.Cli;

/// <summary>
/// What <c>hound onboard &lt;system&gt;</c> tells a person before it asks: the steps ahead, and for every opening question what it is for,
/// with an example. Text only.
/// </summary>
internal static class OnboardGuide
{
    /// <summary>Each opening question, in the order asked, with its hint. <c>{supplier}</c> and <c>{system}</c> stand for the names given.</summary>
    public static readonly IReadOnlyList<(string Question, string Hint)> Opening =
    [
        ("Your name, recorded as who accepted each decision", "Your own name goes on each decision in the onboarding record, e.g. Sam Example"),
        ("System owner's email, as a CISO Assistant user (hound matches the owner by email)", "Whoever answers for the system; they need a user in CISO Assistant, e.g. it@example.com"),
        ("Product, as the vendor names it", "The product's full name, so the right documents and API are found, e.g. Example Asset API, cloud edition"),
        ("Supplier ref_id", "A short code for who provides it; one supplier serves all its systems, e.g. EXAMPLE gives SUP-EXAMPLE"),
        ("Supplier name", "The supplier's legal or trading name, e.g. Example Assets Ltd"),
        ("Solution ref_id (what the supplier provides)", "A short code for the service you buy from the supplier, e.g. EXAMPLE-ASSETS gives SOL-EXAMPLE-ASSETS"),
        ("Solution name", "What the service is called, e.g. Asset inventory"),
        ("Contract ref_id", "A short code for the agreement that covers it, with its year when it renews, e.g. EXAMPLE-2026 gives CTR-EXAMPLE-2026"),
        ("Contract name", "The agreement as your records name it, e.g. Asset inventory subscription 2026"),
        ("What does {supplier} provide for {system}", "The service, and what depends on it, e.g. \"Perimeter firewall; all office internet traffic passes through it\""),
        ("A vendor document to keep (https URL or file; empty to finish)", "A certificate, audit report or security page that backs the supplier's review, kept with its hash, e.g. https://example.com/trust/iso27001.pdf"),
    ];

    /// <summary>The steps, as <c>--help</c> lists them.</summary>
    public static readonly string Steps = """
        1. The supplier: who provides the system, with a draft contract and review. At a terminal, hound stops here until the supplier is approved; an agent's pending and decide go on, and hound approve asks for the supplier before the system.
        2. Applicability: one decision for each requirement in scope (API check, manual evidence, not applicable, or met by another system).
        3. The files: the system's audit, the system itself and the onboarding record, written for you to review.
        """.ReplaceLineEndings("\n");

    public static string Intro(string system) =>
        $"Onboarding {system} in three steps:\n{Steps}\nNothing reaches CISO Assistant until 'hound scope apply'. Stop at any time; the same command carries on where you left off.\n";

    public static readonly string NoSpec =
        "No --spec was given, so hound cannot list what the system's API offers, and API checks are yours to find. If the system has an API specification, stop now (Ctrl+C) and run again with --spec <file>.\n";

    public static string Question(int index, string supplier = "", string system = "") =>
        Opening[index].Question.Replace("{supplier}", supplier, StringComparison.Ordinal).Replace("{system}", system, StringComparison.Ordinal);
}
