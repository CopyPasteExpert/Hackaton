# Compares local models on Bulgarian nicknames through the real /api/search.
# Usage: powershell -File db\model-check.ps1 [-Models qwen2.5:1.5b,gemma2:2b,qwen2.5:3b]
param(
    [string[]]$Models = @('qwen2.5:1.5b', 'gemma2:2b', 'qwen2.5:3b', 'qwen2.5:7b')
)

$ErrorActionPreference = 'Stop'

# With -File, "a,b" arrives as one string.
$Models = $Models | ForEach-Object { $_ -split ',' } | Where-Object { $_ }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# None of these nicknames appear in the prompt's examples.
$cases = [ordered]@{
    'Ваньо Петров'   = 'Иван Петров'
    'Митко Стоянов'  = 'Димитър Стоянов'
    'Пешо Николов'   = 'Петър Николов'
    'Тошко Василев'  = 'Тодор Василев'
    'Ани Георгиева'  = 'Анна Георгиева'
    'Стефчо Колев'   = 'Стефан Колев'
    'Ники Христов'   = 'Николай Христов'
    'Ели Тодорова'   = 'Елена Тодорова'
    'Габи Костова'   = 'Габриела Костова'
    'Деси Маринова'  = 'Десислава Маринова'
    'Любо Пенев'     = 'Любомир Пенев'
    'Кальо Стефанов' = 'Калоян Стефанов'
    # First name only: the surname cannot carry a weak first-name match.
    'Митко'          = 'Димитър Стоянов'
    'Тошко'          = 'Тодор Василев'
    'Габи'           = 'Габриела Костова'
    'Любо'           = 'Любомир Пенев'
    'Деси'           = 'Десислава Маринова'
    # Free text around the name.
    'казвам се Тошко Василев'            = 'Тодор Василев'
    'аз съм Ники Христов'                = 'Николай Христов'
    'търся резултатите на Деси Маринова' = 'Десислава Маринова'
    'здравей, аз съм Пешо Николов'       = 'Петър Николов'
    # Not in the database: must find nobody.
    'Зорница Кирова' = '-'
}

$root = Split-Path $PSScriptRoot -Parent
$port = 5099

function Test-Run([string]$label, [hashtable]$envVars) {
    foreach ($k in $envVars.Keys) { Set-Item "env:$k" $envVars[$k] }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'

    $app = Start-Process dotnet -PassThru -WindowStyle Hidden -ArgumentList @(
        'run', '--project', "$root\Tracker", '--no-build', '--no-launch-profile',
        '--urls', "http://localhost:$port")

    try {
        $ready = $false
        for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
            Start-Sleep -Seconds 1
            try { Invoke-WebRequest "http://localhost:$port/" -UseBasicParsing | Out-Null; $ready = $true } catch {}
        }
        if (-not $ready) { throw "App did not start" }

        # Let the warm-up request load the model before timing anything.
        Start-Sleep -Seconds 15

        $correct = 0
        $wrongWithAi = 0
        $totalMs = 0
        foreach ($query in $cases.Keys) {
            $url = "http://localhost:$port/api/search?q=$([uri]::EscapeDataString($query))"
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $r = Invoke-RestMethod $url
            $ms = $watch.ElapsedMilliseconds
            $first = if ($r.students.Count -gt 0) { $r.students[0].name } else { '-' }
            $totalMs += $ms
            $ok = $first -eq $cases[$query]
            if ($ok) { $correct++ } elseif ($r.aiUsed) { $wrongWithAi++ }
            '{0,-36} -> {1,-20} {2} {3,5} ms {4}' -f $query, $first, ($(if ($ok) { 'OK ' } else { 'BAD' })), $ms, $r.aiNote
        }
        "== $label : $correct / $($cases.Count) верни, $wrongWithAi грешни с 🤖, средно $([math]::Round($totalMs / $cases.Count)) ms`n"
    }
    finally {
        Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
        Get-CimInstance Win32_Process -Filter "Name = 'Tracker.exe'" |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    }
}

dotnet build "$root\Tracker" -v q | Out-Null

Test-Run 'без ИИ' @{ Ollama__Enabled = 'false' }

foreach ($model in $Models) {
    Test-Run $model @{ Ollama__Enabled = 'true'; Ollama__Model = $model; Ollama__TimeoutSeconds = '10' }
}
