# Runs the complete measurement in Release several times and writes the results to results/.
#   performance-repeats.csv  every run, with a Run column
#   performance.csv          the last run; this is the one reported in the thesis
#   fault-tolerance*.csv     from the last run; these counts do not vary between runs
#   environment.txt          machine, runtime, configuration and command
param(
    [int]$Runs = 5
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Measurements"
$results = Join-Path $repoRoot "results"
$temp = Join-Path ([IO.Path]::GetTempPath()) ("measurements-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null

try {
    dotnet build $project -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "Build failed" }

    $repeats = @()
    for ($run = 1; $run -le $Runs; $run++) {
        Write-Output "Run $run of $Runs"
        dotnet run -c Release --no-build --project $project -- $temp | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Measurement run $run failed" }

        $lines = [IO.File]::ReadAllLines((Join-Path $temp "performance.csv"))
        if ($run -eq 1) { $repeats += "Run," + $lines[0] }
        $repeats += $lines | Select-Object -Skip 1 | ForEach-Object { "$run,$_" }
    }

    [IO.File]::WriteAllLines((Join-Path $results "performance-repeats.csv"), [string[]]$repeats)
    Copy-Item (Join-Path $temp "*.csv") $results -Force

    $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim()
    $os = Get-CimInstance Win32_OperatingSystem
    $ramGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
    $runtime = (dotnet --list-runtimes | Select-String "Microsoft.NETCore.App 10\." | Select-Object -Last 1).Line.Split(" ")[1]
    [IO.File]::WriteAllLines((Join-Path $results "environment.txt"), [string[]]@(
        "Date: $(Get-Date -Format 'yyyy-MM-dd')",
        "CPU: $cpu",
        "Memory: $ramGb GB",
        "OS: $($os.Caption) $($os.Version)",
        ".NET SDK: $(dotnet --version)",
        ".NET runtime: $runtime",
        "Configuration: Release",
        "Command: powershell -File Measurements/run-measurements.ps1 -Runs $Runs"
    ))
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Output "Wrote $Runs runs to $results"
