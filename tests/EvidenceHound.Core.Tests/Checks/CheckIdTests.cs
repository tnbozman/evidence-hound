using EvidenceHound.Core.Checks;

namespace EvidenceHound.Core.Tests.Checks;

public class CheckIdTests
{
    [Theory]
    [InlineData("identity.mfa.admins")]
    [InlineData("backup.restore_tested")]
    [InlineData("patching.os.critical_48h")]
    [InlineData("endpoint")]
    public void Accepts_lowercase_dotted_segments(string value)
    {
        var id = CheckId.Create(value);

        id.Value().Value.ShouldBe(value);
        id.Value().ToString().ShouldBe(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Identity.mfa")]
    [InlineData("identity.MFA")]
    [InlineData("identity.mfa.")]
    [InlineData(".identity")]
    [InlineData("identity..mfa")]
    [InlineData("identity mfa")]
    [InlineData("identity.9mfa")]
    [InlineData("identity-mfa")]
    public void Rejects_empty_uppercase_or_trailing_dot(string value)
    {
        var problems = CheckId.Create(value).Problems();

        problems.Single().Code.ShouldBe("check_id.invalid");
        problems.Single().Message.ShouldContain($"'{value}'");
    }

    [Fact]
    public void Rejects_null_and_overlong_ids()
    {
        CheckId.Create(null!).Problems().Single().Code.ShouldBe("check_id.invalid");
        CheckId.Create("a." + new string('b', 127)).Problems().Single().Code.ShouldBe("check_id.invalid");
    }

    [Fact]
    public void Equal_values_are_equal_ids()
    {
        CheckTestData.Id("identity.mfa.admins").ShouldBe(CheckTestData.Id("identity.mfa.admins"));
        CheckTestData.Id("identity.mfa.admins").ShouldNotBe(CheckTestData.Id("identity.mfa.users"));
    }
}
