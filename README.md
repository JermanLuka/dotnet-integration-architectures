# Integration Patterns in .NET: Hexagonal vs. Layered Monolith vs. Message-Driven Architecture

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![tests](https://github.com/JermanLuka/integracijski-vzorci-diploma/actions/workflows/tests.yml/badge.svg)](https://github.com/JermanLuka/integracijski-vzorci-diploma/actions/workflows/tests.yml)

One C# / .NET 10 integration service built three ways (a layered monolith, a hexagonal architecture with ports and adapters, and a message-driven pipeline on `System.Threading.Channels`) and measured for extensibility, code complexity, performance and fault tolerance, using integration patterns such as adapter, canonical data model, producer-consumer, retry and dead-letter queue.

A diploma project that compares three software architectures for an integration service. The service collects metrics from multiple public APIs and forwards them to a target monitoring system. The same task is implemented three times using different architectural approaches, then measured across four quality metrics.

## Results

| Metric | Monolith (layered) | Hexagonal (ports & adapters) | Messaging (Channels) |
|---|:---:|:---:|:---:|
| Lines added to existing files to add a 4th source | 22 | 2 | 6 |
| Core logic changed to add a 4th source | Yes | No | No |
| Maintainability index (higher is better) | 55 | 72 | 65 |
| Cyclomatic complexity | 25 | 44 | 56 |
| Lines of code | 216 | 270 | 439 |
| Mean processing time, S (2 repos) | 0.070 ms | 0.083 ms | 0.126 ms |
| Mean processing time, M (50 repos) | 0.109 ms | 0.115 ms | 0.141 ms |
| Mean processing time, L (500 repos) | 0.295 ms | 0.259 ms | 0.166 ms |
| Mean processing time, XL (5000 repos) | 0.744 ms | 0.708 ms | 0.765 ms |
| Metrics delivered under transient outage (of 15) | 10–12 | 10–12 | 15 |

Sources: [`extensibility.csv`](results/extensibility.csv), [`complexity.csv`](results/complexity.csv),
[`performance.csv`](results/performance.csv) and
[`fault-tolerance-transient.csv`](results/fault-tolerance-transient.csv). Processing time is the
mean of 2000 iterations against faked, in-memory HTTP responses, so it measures architectural
overhead plus JSON parsing, not network time; the size is the number of repositories in the
GitHub response. The table shows the last of [five runs](results/performance-repeats.csv): at S
the monolith and hexagonal swap places between runs, and at XL the ranges of all three overlap.
The transient-outage row is the range over the four sources failing one at a time. Under a
permanent outage all three deliver every metric from the working sources
(see [Fault Tolerance](#fault-tolerance)).

- **H1 (extensibility): confirmed.** Adding a source took 2 lines in existing files with
  hexagonal vs. 22 with the monolith, and only the monolith's core changed. Messaging behaved
  like hexagonal, so the gain comes from separating the core from the sources, not from
  hexagonal alone.
- **H2 (fault tolerance): conditionally confirmed.** Under a transient outage messaging
  delivered all 15 metrics vs. 10–12 for the monolith; under a permanent outage all three were
  equal. The advantage comes from fetch retries, which only messaging implements and which
  could be added to the other two.
- **H3 (complexity and performance): partially confirmed.** It held for cyclomatic complexity
  and lines of code, but not for maintainability index (both abstracted designs scored higher
  than the monolith) or for processing time at every payload size (messaging was slowest at 2
  and 50 repos but fastest at 500).

Full statements of the hypotheses are under [Hypotheses](#hypotheses).

## Architectures

- **Monolith (Layered)** - Simple sequential pipeline with no abstractions. Serves as the baseline.
- **Hexagonal (Ports & Adapters)** - Core logic separated from external dependencies through interfaces. Emphasis on extensibility.
- **Messaging (Channel-based)** - Fetching and sending decoupled via in-process message channels (`System.Threading.Channels`). Emphasis on fault tolerance.

All three architectures share the same `Shared` module (domain model, data sources, target client) and differ only in how they compose these building blocks.

## Key design decisions

- **In-process `System.Threading.Channels` instead of a message broker.** The messaging variant
  needs one property, producers decoupled from consumers, and a bounded channel provides it
  without RabbitMQ or Kafka infrastructure that the comparison doesn't need. Four producers write
  to a channel with capacity 100 (writers wait when it is full), and two consumers read from it
  and send in batches of five. The trade-off is durability: an in-memory queue does not survive
  a restart the way a broker does.
- **Retry with exponential backoff only in the messaging variant.** A producer's fetch is a
  self-contained, repeatable step, so retry fits there naturally:
  [`FetchRetryPolicy`](ArchC.Messaging/Producers/FetchRetryPolicy.cs) makes up to 3 attempts with
  50 ms and 100 ms delays, and consumers retry failed sends (100 ms, 200 ms) before moving the
  batch to a dead-letter queue. The monolith and hexagonal variants deliberately fetch once, so
  the transient-outage scenario isolates what retries contribute. It also means that experiment
  compares retry against no retry, not messaging against layering (see H2).
- **Faked HTTP for deterministic measurements.** [`FakeHttpHandler`](Shared.Tests/FakeHttpHandler.cs)
  serves canned JSON per path and query and can return 503 for a source's first N requests to
  simulate a transient outage; [`MockTargetClient`](Shared/Target/MockTargetClient.cs) records
  what was sent in memory. Every run is reproducible and unaffected by network jitter, API
  availability or rate limits, and sub-millisecond architectural overhead isn't drowned out by
  network latency. The cost is that the numbers say nothing about behaviour under real network
  conditions.

## Data Sources

| Source | Auth |
|---|---|
| GitHub | API token |
| StackOverflow | API key |
| World Bank | None |
| OpenMeteo | None (added as 4th source for extensibility measurement) |

## Hypotheses

- **H1**: Hexagonal architecture requires fewer changes to existing code when adding a new source than the monolith.
- **H2**: Messaging architecture delivers a higher percentage of metrics when a source fails than the synchronous monolith.
- **H3**: Greater architectural abstraction increases code complexity and processing time compared to the monolith.

## Project Structure

```
Shared/                     Shared domain model, data sources, target client
Shared.Tests/               Unit tests for shared components

ArchA.Hexagonal/            Hexagonal architecture (Ports & Adapters)
ArchA.Hexagonal.Tests/      Tests for hexagonal architecture

ArchB.Monolith/             Layered monolith architecture
ArchB.Monolith.Tests/       Tests for monolith architecture

ArchC.Messaging/            Message-based architecture (Channels)
ArchC.Messaging.Tests/      Tests for messaging architecture

Measurements/               Performance and fault tolerance runners,
                           plus run-measurements.ps1 and measure-extensibility.ps1
results/                    Output of the measurements:
                             extensibility.csv, complexity.csv,
                             performance.csv, performance-repeats.csv,
                             fault-tolerance.csv, fault-tolerance-transient.csv,
                             environment.txt, tests.txt
```

## Running

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The measurement
scripts run in Windows PowerShell.

```bash
# Build
dotnet build

# Run tests (the last result is saved in results/tests.txt)
dotnet test -c Release

# Run the complete measurement five times in Release (writes results/)
powershell -File Measurements/run-measurements.ps1
```

## Tech Stack

- C# / .NET 10
- xUnit (testing)
- System.Threading.Channels (messaging architecture)

## Measurements

All measurements run against `FakeHttpHandler` and `MockTargetClient` for deterministic, network-free results.

### Extensibility

Extensibility is measured as the size of the change required to add a fourth data
source (OpenMeteo) to each architecture. It is computed by
`Measurements/measure-extensibility.ps1` from the git diff of commit `e2de80d`,
which is the commit that added that source.

#### Counting rule

The numbers in `results/extensibility.csv` follow five rules. They are stated here
so that every number in the thesis can be re-derived rather than taken on trust.

1. **New files and existing files are counted separately.** `LinesAddedInNewFiles`
   counts lines in files the commit adds; `LinesAddedInExistingFiles` counts
   lines added to files that already existed. Deleted lines are not counted. Only
   the second number describes how far the change reached into working code.
2. **Files whose only change is a comment are excluded.** Commit `e2de80d` also
   replaced em dashes with hyphens in doc comments across several files. Those
   changes have nothing to do with adding a source, so files that contain no other
   change are skipped. This excludes all three files under
   `ArchA.Hexagonal/Core/` and `Shared/Target/MockTargetClient.cs`.
3. **Registering a new source among the existing ones is not a change to core
   logic.** A core change means the processing logic itself changed — the code
   that fetches, merges and forwards metrics. Adding one more entry to a list of
   sources that the core already iterates over is not that.
4. **Counting is per project.** The shared cost — `Shared/Sources/OpenMeteoSource.cs`,
   61 lines — is identical for all three architectures and is reported on its own
   `Shared (common)` row, so the per-architecture rows show only the additional cost
   of wiring that source into each architecture.
5. **Lines unrelated to adding the source are excluded.** Commit `e2de80d` also added
   the fail-fast switch for the fault-tolerance experiment to the monolith's
   `DataAccess.cs`. Nine of its 26 added lines belong to that switch: the `_failFast`
   field, the constructor parameter and the assignment (3), plus the changed `catch`
   and log lines of the three sources that already existed (2 x 3). The script
   subtracts them, leaving the 17 lines that add `FetchOpenMeteoAsync`. Check them with
   `git show e2de80d -- ArchB.Monolith/DataAccess.cs`.

#### What counts as the core, by path

| Architecture | Core files | Touched by `e2de80d`? |
|---|---|---|
| Monolith | `ArchB.Monolith/MetricOrchestrator.cs`, `ArchB.Monolith/DataAccess.cs` | **Yes** — both |
| Hexagonal | `ArchA.Hexagonal/Core/` (whole directory) | No — comment-only |
| Messaging | `ArchC.Messaging/MetricConsumer.cs`, `ArchC.Messaging/Producers/FetchRetryPolicy.cs` | No |

The hexagonal implementation names its core directory `Core`, so for it the
question is simply whether the diff touched that path. It did not, other than the
comment-only change excluded by rule 2.

The messaging implementation is the one case that needs a note.
`ArchC.Messaging/MessagingOrchestrator.cs` *was* modified, by four lines: a field,
a constructor parameter, an assignment, and one more entry in the producer array
awaited by `Task.WhenAll`. Under rule 3 that is registration, not processing
logic, and the class that does the processing — `MetricConsumer.cs` — was not
touched. The four lines are still counted in `LinesAddedInExistingFiles`, so
they are visible in the number; they just do not flip `CoreLogicChanged` to `Yes`.
The monolith's four lines in its orchestrator are a different kind of change: they
insert a call, a log statement and a merge into the body of the processing
routine, because that body is where the monolith enumerates its sources.

#### Reproducing the numbers

```bash
# The full change, restricted to the architecture and shared projects
git show e2de80d -- ArchA.Hexagonal ArchB.Monolith ArchC.Messaging Shared

# Per-file line counts (added / removed / path)
git diff --numstat e2de80d~1 e2de80d -- ArchA.Hexagonal ArchB.Monolith ArchC.Messaging Shared

# Regenerate results/extensibility.csv from scratch
powershell -File Measurements/measure-extensibility.ps1
```

#### Where each number comes from

| Row | Files modified | Lines added in existing files | Made up of | New file |
|---|---|---|---|---|
| Monolith | 3 | **22** | `DataAccess.cs` 17 (26 minus 9, rule 5) + `MetricOrchestrator.cs` 4 + `appsettings.json` 1 | — |
| Hexagonal | 2 | **2** | `Program.cs` 1 + `appsettings.json` 1 | `Adapters/OpenMeteoAdapter.cs` 35 |
| Messaging | 3 | **6** | `MessagingOrchestrator.cs` 4 + `Program.cs` 1 + `appsettings.json` 1 | `Producers/OpenMeteoProducer.cs` 45 |
| Shared (common) | 1 | **1** | `Target/RealTargetClient.cs` 1 | `Sources/OpenMeteoSource.cs` 61 |

The single line on the `Shared` row is an em-dash inside a log message string in
`RealTargetClient.cs`. Rule 2 does not exclude it, because it is not a comment,
but it is unrelated to adding a source. It is reported as measured rather than
removed by hand.

#### What this metric is and is not

Lines added in existing files are not a measure of architectural quality. They are a
proxy for how far a change reaches into working code, and only meaningful when comparing
implementations that solve the same task, in the same language, in the same
codebase — which is the case here. It says nothing about whether the resulting
code is good, and a small diff can still be a dangerous one.

### Code Complexity

Computed using Visual Studio Code Metrics (View > Other Windows > Code Metrics). Records maintainability index, cyclomatic complexity, class coupling, and lines of code per architecture project.

### Performance

`PerformanceRunner.cs` uses `Stopwatch` to measure each architecture over 2000
iterations. Pipeline construction happens outside the timed region, so only the
execution of `RunAsync` is measured. Reported statistics are mean, standard
deviation, median and 95th percentile in milliseconds.

The measurement is a matrix of **three architectures × four response sizes**. The
size axis scales one response only — the GitHub repository listing
(`/user/repos`), generated by `TestData.GitHubReposResponse` — so only one factor
changes. The four steps are S = 2, M = 50, L = 500 and XL = 5000 repositories. L and
XL are generated payloads for testing the effect of size: the live source requests a
single page of at most 100 repositories. The number of metrics does not change with
size: stars and forks are summed across the listing, so GitHub always yields five
metrics and the run always yields fifteen.

Because sources are faked in memory, the numbers reflect architectural overhead
plus deserialisation cost, not real network behaviour. The fake handler returns
completed tasks and parsing is synchronous, so these measurements do not show that
the parsing of different sources overlaps. The cause of the rankings below was not
verified. Architectures are measured in a fixed order.

`results/performance-repeats.csv` holds five runs of the same measurement, and
`results/performance.csv` is the last of them. In all five runs the messaging
implementation is the slowest at S and M and the fastest at L. At M and L the ranges
of the five run means do not overlap between architectures. The monolith and the
hexagonal implementation swap places at S; the monolith is faster at M and the
hexagonal implementation is faster at L and XL. At XL the ranges of all three overlap,
and the messaging implementation and the monolith swap places.

`results/environment.txt` records the machine, runtime, configuration and command.

### Fault Tolerance

`FaultToleranceRunner.cs` measures how many metrics are delivered under two scenarios. The two CSV files use different denominators: `fault-tolerance.csv` reports the percentage of metrics expected from the *working* sources (10 to 12), and `fault-tolerance-transient.csv` the percentage of the normal total of 15. The thesis avoids this difference by reporting `ActualMetrics` out of 15 for both scenarios.

**Scenario A (permanent outage):** one source is down for the entire run. Every implementation that catches errors per source delivers all metrics from the working sources (10 to 12 of 15), so this scenario does not differentiate the architectures.

**Scenario B (transient outage):** one source rejects its first request and then recovers. Only the messaging implementation retries fetches (`FetchRetryPolicy`, up to 3 attempts with exponential backoff) and delivers 100%. The monolith and hexagonal implementations fetch once and lose the metrics of that source for the cycle. This asymmetry is deliberate: the retry mechanism is the measured difference (see H2).

The monolith is additionally tested in a mode where exceptions propagate and the run crashes, labeled "Monolith (fail-fast)" in the results.

## Author

**Luka Jerman**

- Website: [lukajerman.dev](https://lukajerman.dev)
- LinkedIn: [linkedin.com/in/luka-jerman-12046442b](https://www.linkedin.com/in/luka-jerman-12046442b)

## License

The code is released under the [MIT License](LICENSE).
