using System.Security.Cryptography;
using EvidenceHound.Core.Checks;
using EvidenceHound.Core.Publishing;
using EvidenceHound.Core.Tests.Checks;

namespace EvidenceHound.Core.Tests.Publishing;

public class EvidenceNamesTests
{
    [Fact]
    public void Collector_scoped_id_is_the_name() =>
        EvidenceNames.For(Evidence("example-fake/mfa-users.json", "mfa-users.json")).ShouldBe("example-fake/mfa-users.json");

    [Fact]
    public void Other_ids_are_added_to_the_file_name() =>
        EvidenceNames.For(Evidence("ev-0001", "mfa-admins.json")).ShouldBe("mfa-admins.json (ev-0001)");

    [Fact]
    public void Same_file_name_from_two_collectors_gives_two_names() =>
        EvidenceNames.For(Evidence("collector-a/report.json", "report.json"))
            .ShouldNotBe(EvidenceNames.For(Evidence("collector-b/report.json", "report.json")));

    private static Evidence Evidence(string id, string fileName) =>
        Core.Checks.Evidence.Create(id, fileName, "application/json", Convert.ToHexStringLower(SHA256.HashData([1])), 1, CheckTestData.CollectedAt).Value();
}
