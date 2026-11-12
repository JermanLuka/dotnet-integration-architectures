using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Shared.Tests;

/// <summary>
/// Single source of truth for test data: the fake API responses, their URLs,
/// and the test configuration used by every test and measurement project.
/// URLs are stored in the order each source actually requests them, so the
/// first entry is always the first request a source makes.
/// The GitHub repository listing can be generated at a requested size, so the
/// same pipelines can be measured against responses of different sizes.
/// </summary>
public static class TestData
{
    public const int TotalMetrics = 15;

    public static readonly string[] SourceNames = ["GitHub", "StackOverflow", "WorldBank", "OpenMeteo"];

    public static readonly IReadOnlyDictionary<string, int> MetricCountBySource = new Dictionary<string, int>
    {
        ["GitHub"] = 5,
        ["StackOverflow"] = 4,
        ["WorldBank"] = 3,
        ["OpenMeteo"] = 3,
    };

    /// <summary>
    /// Number of repositories in the default GitHub repository listing. Keeping the
    /// default at 2 means every existing test and measurement sees the response it
    /// always saw.
    /// </summary>
    public const int DefaultRepoCount = 2;

    /// <summary>One step of the response-size sweep: a label for reporting and the repository count it stands for.</summary>
    public readonly record struct ResponseSize(string Label, int RepoCount);

    /// <summary>
    /// The response sizes the performance matrix is measured at. S reproduces the
    /// original fixed response. L and XL are generated payloads for testing the effect
    /// of size only: the live source requests a single page of at most 100 repositories.
    /// </summary>
    public static readonly ResponseSize[] ResponseSizes =
    [
        new("S", 2),
        new("M", 50),
        new("L", 500),
        new("XL", 5000),
    ];

    private const string GitHubReposEndpoint = "/user/repos?per_page=100&type=owner";

    private static string WorldBankResponse(double value) =>
        "[{\"page\":1,\"pages\":1,\"total\":1},[{\"value\":" + value.ToString("G", CultureInfo.InvariantCulture) + "}]]";

    /// <summary>
    /// Builds a GitHub repository listing with <paramref name="repoCount"/> entries.
    /// Star and fork counts follow a fixed arithmetic progression, so the response is
    /// deterministic and the first two entries are byte-for-byte the original response.
    /// Only the payload grows; GitHub still yields exactly five metrics, because
    /// stars and forks are summed across the listing.
    /// </summary>
    public static string GitHubReposResponse(int repoCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(repoCount);

        var sb = new StringBuilder(repoCount * 48 + 2);
        sb.Append('[');
        for (int i = 0; i < repoCount; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("{\"stargazers_count\":").Append(3 + 4 * i)
              .Append(",\"forks_count\":").Append(1 + i).Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static readonly Dictionary<string, (string Endpoint, string Response)[]> Endpoints = new()
    {
        ["GitHub"] =
        [
            ("/user", """{"public_repos":5,"followers":10,"public_gists":2}"""),
            (GitHubReposEndpoint, GitHubReposResponse(DefaultRepoCount)),
        ],
        ["StackOverflow"] =
        [
            ("/2.3/me?site=stackoverflow&key=test-key&access_token=test-token&filter=default",
                """{"items":[{"reputation":1234,"badge_counts":{"gold":1,"silver":5,"bronze":20}}]}"""),
        ],
        ["WorldBank"] =
        [
            ("/v2/country/SVN/indicator/NY.GDP.MKTP.CD?format=json&per_page=1&mrv=1", WorldBankResponse(54000000000)),
            ("/v2/country/SVN/indicator/SP.POP.TOTL?format=json&per_page=1&mrv=1", WorldBankResponse(2100000)),
            ("/v2/country/SVN/indicator/SP.DYN.LE00.IN?format=json&per_page=1&mrv=1", WorldBankResponse(81.2)),
        ],
        ["OpenMeteo"] =
        [
            ("/v1/forecast?latitude=46.05&longitude=14.51&current=temperature_2m,relative_humidity_2m,wind_speed_10m",
                """{"current":{"temperature_2m":22.5,"relative_humidity_2m":65,"wind_speed_10m":12.3}}"""),
        ],
    };

    private static readonly Dictionary<string, (string Key, string Value)[]> ConfigKeys = new()
    {
        ["GitHub"] = [("GitHub:Token", "test-gh-token")],
        ["StackOverflow"] = [("StackOverflow:AccessToken", "test-token"), ("StackOverflow:ApiKey", "test-key")],
        ["WorldBank"] = [("WorldBank:CountryCode", "SVN")],
        ["OpenMeteo"] = [("OpenMeteo:Latitude", "46.05"), ("OpenMeteo:Longitude", "14.51")],
    };

    public static string FirstEndpoint(string source) => Endpoints[source][0].Endpoint;

    public static Dictionary<string, string> AllResponses() => ResponsesFor(SourceNames);

    public static Dictionary<string, string> AllResponses(int repoCount) => ResponsesFor(SourceNames, repoCount);

    public static Dictionary<string, string> ResponsesFor(params string[] sources) =>
        ResponsesFor(sources, DefaultRepoCount);

    public static Dictionary<string, string> ResponsesFor(string[] sources, int repoCount)
    {
        var responses = new Dictionary<string, string>();
        foreach (var source in sources)
            foreach (var (endpoint, response) in Endpoints[source])
                responses[endpoint] = response;

        if (repoCount != DefaultRepoCount && responses.ContainsKey(GitHubReposEndpoint))
            responses[GitHubReposEndpoint] = GitHubReposResponse(repoCount);

        return responses;
    }

    public static Dictionary<string, string> ResponsesExcept(string failingSource) =>
        ResponsesFor(SourceNames.Where(s => s != failingSource).ToArray());

    public static IConfigurationRoot Config() => ConfigFor(SourceNames);

    public static IConfigurationRoot ConfigFor(params string[] sources)
    {
        var values = new Dictionary<string, string?>();
        foreach (var source in sources)
            foreach (var (key, value) in ConfigKeys[source])
                values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
