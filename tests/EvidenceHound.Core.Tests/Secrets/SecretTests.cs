using System.Globalization;
using EvidenceHound.Core.Secrets;

namespace EvidenceHound.Core.Tests.Secrets;

public class SecretTests
{
    [Fact]
    public void ToString_is_redacted()
    {
        var secret = new Secret("hunter2");

        secret.ToString().ShouldBe("***");
        string.Create(CultureInfo.InvariantCulture, $"token {secret}").ShouldBe("token ***");
    }

    [Fact]
    public void Reveal_returns_the_value()
    {
        new Secret("hunter2").Reveal().ShouldBe("hunter2");
    }

    [Fact]
    public void Null_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => new Secret(null!));
    }
}
