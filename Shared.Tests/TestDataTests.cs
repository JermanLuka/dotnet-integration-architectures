using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Sources;

namespace Shared.Tests;

/// <summary>
/// Guards the response-size parametrisation: growing the GitHub repository listing
/// must change only the payload, never the number of metrics, and the default size
/// must reproduce the fixed response the earlier measurements ran against.
/// </summary>
public class TestDataTests
{
    private const string ReposEndpoint = "/user/repos?per_page=100&type=owner";

    private const string OriginalReposResponse =
        """[{"stargazers_count":3,"forks_count":1},{"stargazers_count":7,"forks_count":2}]""";

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(5000)]
    public void GitHubReposResponse_ContainsRequestedNumberOfRepositories(int repoCount)
    {
        using var doc = JsonDocument.Parse(TestData.GitHubReposResponse(repoCount));

        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(repoCount, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void DefaultSize_ReproducesTheOriginalFixedResponse()
    {
        Assert.Equal(OriginalReposResponse, TestData.GitHubReposResponse(TestData.DefaultRepoCount));
        Assert.Equal(OriginalReposResponse, TestData.AllResponses()[ReposEndpoint]);
        Assert.Equal(OriginalReposResponse, TestData.AllResponses(TestData.DefaultRepoCount)[ReposEndpoint]);
    }

    [Fact]
    public void ResponsesFor_WithoutRepoCount_IsUnchanged()
    {
        var withoutCount = TestData.ResponsesFor("GitHub", "WorldBank");
        var withDefaultCount = TestData.ResponsesFor(["GitHub", "WorldBank"], TestData.DefaultRepoCount);

        Assert.Equal(withDefaultCount, withoutCount);
    }

    [Fact]
    public void LargerSize_ChangesOnlyTheRepositoryListing()
    {
        var small = TestData.AllResponses(TestData.DefaultRepoCount);
        var large = TestData.AllResponses(500);

        Assert.Equal(small.Keys.Order(), large.Keys.Order());
        Assert.True(large[ReposEndpoint].Length > small[ReposEndpoint].Length);
        foreach (var key in small.Keys.Where(k => k != ReposEndpoint))
            Assert.Equal(small[key], large[key]);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(5000)]
    public async Task GitHubYieldsFiveMetricsAtEverySize(int repoCount)
    {
        var handler = new FakeHttpHandler(TestData.AllResponses(repoCount));
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.github.com") };
        var source = new GitHubSource(http, "test-gh-token", NullLogger<GitHubSource>.Instance);

        var metrics = await source.FetchMetricsAsync();

        Assert.Equal(TestData.MetricCountBySource["GitHub"], metrics.Count);

        // Stars and forks are summed over the listing, so their values grow with the
        // response size while the number of metrics stays the same.
        var expectedStars = Enumerable.Range(0, repoCount).Sum(i => 3 + 4 * i);
        var expectedForks = Enumerable.Range(0, repoCount).Sum(i => 1 + i);
        Assert.Contains(metrics, m => m.Name == "github.total_stars" && m.Value == expectedStars);
        Assert.Contains(metrics, m => m.Name == "github.total_forks" && m.Value == expectedForks);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    [InlineData(500)]
    [InlineData(5000)]
    public async Task AllFourSourcesTogetherYieldFifteenMetricsAtEverySize(int repoCount)
    {
        var handler = new FakeHttpHandler(TestData.AllResponses(repoCount));

        HttpClient Client(string baseAddress) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri(baseAddress) };

        var github = new GitHubSource(
            Client("https://api.github.com"), "test-gh-token", NullLogger<GitHubSource>.Instance);
        var stackoverflow = new StackOverflowSource(
            Client("https://api.stackexchange.com"), "test-token", "test-key", NullLogger<StackOverflowSource>.Instance);
        var worldbank = new PublicDataSource(
            Client("https://api.worldbank.org"), "SVN", NullLogger<PublicDataSource>.Instance);
        var openmeteo = new OpenMeteoSource(
            Client("https://api.open-meteo.com"), "46.05", "14.51", NullLogger<OpenMeteoSource>.Instance);

        var total =
            (await github.FetchMetricsAsync()).Count +
            (await stackoverflow.FetchMetricsAsync()).Count +
            (await worldbank.FetchMetricsAsync()).Count +
            (await openmeteo.FetchMetricsAsync()).Count;

        Assert.Equal(TestData.TotalMetrics, total);
        Assert.Equal(TestData.TotalMetrics, TestData.MetricCountBySource.Values.Sum());
    }

    [Fact]
    public void ResponseSizes_StartAtTheDefaultAndGrow()
    {
        Assert.Equal(TestData.DefaultRepoCount, TestData.ResponseSizes[0].RepoCount);
        Assert.Equal(
            TestData.ResponseSizes.Select(s => s.RepoCount).Order(),
            TestData.ResponseSizes.Select(s => s.RepoCount));
    }
}
