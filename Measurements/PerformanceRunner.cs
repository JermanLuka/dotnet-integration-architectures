using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Shared.Tests;

namespace Measurements;

public sealed class PerformanceRunner
{
    private readonly IConfigurationRoot _config = TestData.Config();

    public record Result(
        string Architecture,
        double MeanMs,
        double StdDevMs,
        double MedianMs,
        double P95Ms,
        int Iterations,
        string ResponseSize,
        int RepoCount);

    /// <summary>
    /// Measures every architecture at every response size from <see cref="TestData.ResponseSizes"/>.
    /// Sizes are the outer loop, so each size gets its own handler and its own warm-up, and the
    /// rows come out grouped by size with the smallest first.
    /// </summary>
    public async Task<List<Result>> RunAllAsync(int iterations = 2000)
    {
        var results = new List<Result>();

        foreach (var size in TestData.ResponseSizes)
        {
            var handler = new FakeHttpHandler(TestData.AllResponses(size.RepoCount));

            var pipelines = new (string Name, Func<Func<Task>> Prepare)[]
            {
                ("Monolith (Layered)", () => PrepareMonolith(handler)),
                ("Hexagonal (Ports & Adapters)", () => PrepareHexagonal(handler)),
                ("Messaging (Channel-based)", () => PrepareMessaging(handler)),
            };

            const int warmupIterations = 10;
            for (int i = 0; i < warmupIterations; i++)
                foreach (var (_, prepare) in pipelines)
                    await prepare()();

            foreach (var (name, prepare) in pipelines)
                results.Add(await MeasureAsync(name, prepare, iterations, size));
        }

        return results;
    }

    private static async Task<Result> MeasureAsync(
        string name, Func<Func<Task>> prepare, int iterations, TestData.ResponseSize size)
    {
        for (int i = 0; i < 10; i++)
            await prepare()();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var times = new double[iterations];
        var sw = new Stopwatch();

        for (int i = 0; i < iterations; i++)
        {
            var run = prepare();
            sw.Restart();
            await run();
            sw.Stop();
            times[i] = sw.Elapsed.TotalMilliseconds;
        }

        var mean = times.Average();
        var stddev = Math.Sqrt(times.Select(t => (t - mean) * (t - mean)).Average());

        var sorted = (double[])times.Clone();
        Array.Sort(sorted);
        var median = Percentile(sorted, 50);
        var p95 = Percentile(sorted, 95);

        return new Result(name, Math.Round(mean, 3), Math.Round(stddev, 3),
            Math.Round(median, 3), Math.Round(p95, 3), iterations, size.Label, size.RepoCount);
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var rank = percentile / 100.0 * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }

    private Func<Task> PrepareMonolith(HttpMessageHandler handler)
    {
        var (orchestrator, _) = ArchitectureFactory.BuildMonolith(_config, handler);
        return () => orchestrator.RunAsync();
    }

    private Func<Task> PrepareHexagonal(HttpMessageHandler handler)
    {
        var (orchestrator, _) = ArchitectureFactory.BuildHexagonal(_config, handler);
        return () => orchestrator.RunAsync();
    }

    private Func<Task> PrepareMessaging(HttpMessageHandler handler)
    {
        var (orchestrator, _) = ArchitectureFactory.BuildMessaging(_config, handler);
        return () => orchestrator.RunAsync();
    }

    public static void WriteCsv(List<Result> results, string csvPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

        using var writer = new StreamWriter(csvPath);
        writer.WriteLine("Architecture,MeanMs,StdDevMs,MedianMs,P95Ms,Iterations,ResponseSize,RepoCount");
        foreach (var r in results)
        {
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7}",
                r.Architecture, r.MeanMs, r.StdDevMs, r.MedianMs, r.P95Ms, r.Iterations, r.ResponseSize, r.RepoCount));
        }
    }
}
