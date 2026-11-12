param(
    [string]$BaseCommit = "e2de80d~1",
    [string]$Commit = "e2de80d",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel).Trim()
if ($OutputPath -eq "") {
    $OutputPath = Join-Path $repoRoot "results/extensibility.csv"
}

Push-Location $repoRoot
try {
    $scopes = @(
        @{ Label = "Shared (common)"; Path = "Shared/"; Core = $null },
        @{ Label = "Monolith"; Path = "ArchB.Monolith/"; Core = @("ArchB.Monolith/DataAccess.cs", "ArchB.Monolith/MetricOrchestrator.cs") },
        @{ Label = "Hexagonal"; Path = "ArchA.Hexagonal/"; Core = @("ArchA.Hexagonal/Core/") },
        @{ Label = "Messaging"; Path = "ArchC.Messaging/"; Core = @("ArchC.Messaging/MetricConsumer.cs", "ArchC.Messaging/Producers/FetchRetryPolicy.cs") }
    )

    # Added lines in commit e2de80d that belong to the fail-fast switch of the fault-tolerance
    # experiment, not to adding the source (README, counting rule 5): the _failFast field,
    # the constructor parameter and the assignment (3), plus the catch and log lines of the
    # three sources that already existed (2 x 3).
    $unrelatedAddedLines = @{ "ArchB.Monolith/DataAccess.cs" = 9 }

    function Test-CommentOnlyChange([string]$file) {
        $diff = git diff -U0 $BaseCommit $Commit -- $file
        foreach ($line in $diff) {
            if ($line -match '^(\+\+\+|---)') { continue }
            if ($line -match '^[+-]') {
                $content = $line.Substring(1).Trim()
                if ($content -ne '' -and -not $content.StartsWith('//')) { return $false }
            }
        }
        return $true
    }

    $rows = @()
    foreach ($scope in $scopes) {
        $numstat = @(git diff --numstat $BaseCommit $Commit -- $scope.Path)
        $status = @(git diff --name-status $BaseCommit $Commit -- $scope.Path)

        $addedByFile = @{}
        foreach ($line in $numstat) {
            $parts = $line -split "`t"
            if ($parts.Count -ge 3 -and $parts[0] -match '^\d+$') {
                $addedByFile[$parts[2]] = [int]$parts[0]
            }
        }

        $filesModified = 0
        $newFiles = 0
        $linesNew = 0
        $linesExisting = 0
        $coreChanged = $false

        foreach ($line in $status) {
            $parts = $line -split "`t"
            $state = $parts[0]
            $file = $parts[1]
            $lines = 0
            if ($addedByFile.ContainsKey($file)) { $lines = $addedByFile[$file] }

            if ($state -eq 'A') {
                $newFiles++
                $linesNew += $lines
            }
            elseif ($state -eq 'M') {
                if (Test-CommentOnlyChange $file) { continue }
                $filesModified++
                if ($unrelatedAddedLines.ContainsKey($file)) { $lines -= $unrelatedAddedLines[$file] }
                $linesExisting += $lines
                if ($null -ne $scope.Core) {
                    foreach ($corePath in $scope.Core) {
                        if ($file -eq $corePath -or $file.StartsWith($corePath)) { $coreChanged = $true }
                    }
                }
            }
        }

        $coreLabel = "N/A"
        if ($null -ne $scope.Core) {
            if ($coreChanged) { $coreLabel = "Yes" } else { $coreLabel = "No" }
        }

        $rows += "{0},{1},{2},{3},{4},{5}" -f $scope.Label, $filesModified, $newFiles, $linesNew, $linesExisting, $coreLabel
    }

    $header = "Architecture,FilesModified,NewFiles,LinesAddedInNewFiles,LinesAddedInExistingFiles,CoreLogicChanged"
    $outDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }
    Set-Content -Path $OutputPath -Value (@($header) + $rows) -Encoding utf8

    Write-Output "Wrote $OutputPath"
    Get-Content $OutputPath
}
finally {
    Pop-Location
}
