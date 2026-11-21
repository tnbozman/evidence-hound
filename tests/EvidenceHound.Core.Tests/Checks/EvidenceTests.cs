using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Tests.Checks;

public class EvidenceTests
{
    [Fact]
    public void Valid_evidence_keeps_every_field_and_lowercases_the_hash()
    {
        var evidence = Evidence.Create(
            "ev-1", "mfa-admins.json", "application/json", CheckTestData.Sha256.ToUpperInvariant(), 512, CheckTestData.CollectedAt).Value();

        evidence.Id.ShouldBe("ev-1");
        evidence.FileName.ShouldBe("mfa-admins.json");
        evidence.MediaType.ShouldBe("application/json");
        evidence.Sha256.ShouldBe(CheckTestData.Sha256);
        evidence.SizeBytes.ShouldBe(512);
        evidence.CollectedAt.ShouldBe(CheckTestData.CollectedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a088")]
    [InlineData("zf86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")]
    public void Sha256_must_be_64_hex_chars(string sha256)
    {
        var result = Evidence.Create("ev-1", "a.json", "application/json", sha256, 1, CheckTestData.CollectedAt);

        result.Problems().Single().Code.ShouldBe("evidence.sha256_invalid");
    }

    [Fact]
    public void Empty_names_and_negative_size_are_rejected_with_every_problem()
    {
        var result = Evidence.Create("", " ", "", CheckTestData.Sha256, -1, CheckTestData.CollectedAt);

        result.Problems().Select(p => p.Code).ShouldBe(
            ["evidence.id_empty", "evidence.file_name_empty", "evidence.media_type_empty", "evidence.size_negative"],
            ignoreOrder: true);
    }
}
