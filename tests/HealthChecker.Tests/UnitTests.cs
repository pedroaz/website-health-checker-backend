using System.Net;
using HealthChecker;
namespace HealthChecker.Tests;

public class UnitTests
{
    [Theory]
    [InlineData(200, true)]
    [InlineData(204, true)]
    [InlineData(299, true)]
    [InlineData(300, false)]
    [InlineData(404, false)]
    [InlineData(503, false)]
    public void ClassifiesStatus(int status, bool healthy) => Assert.Equal(healthy, WebsiteChecker.IsHealthy(status));
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("http://user:secret@example.com")]
    [InlineData("not a URL")]
    public void RejectsInvalidUrls(string url) => Assert.Throws<ArgumentException>(() => UrlPolicy.Parse(url));
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.1.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("fc00::1")]
    [InlineData("100.64.1.1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    public void RejectsPrivateAddresses(string address) => Assert.True(UrlPolicy.IsPrivate(IPAddress.Parse(address)));
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void AllowsPublicAddresses(string address) => Assert.False(UrlPolicy.IsPrivate(IPAddress.Parse(address)));
    [Fact]
    public void SchedulesFromCompletionRatherThanReplayingMissedIntervals()
    {
        var completed = DateTimeOffset.Parse("2026-01-01T12:00:00Z");
        Assert.Equal(completed.AddMinutes(5), CheckService.NextDue(completed));
    }
}
