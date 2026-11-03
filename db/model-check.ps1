# Compares local models on Bulgarian nicknames through the real /api/search.
# Usage: powershell -File db\model-check.ps1 [-Models qwen2.5:1.5b,gemma2:2b,qwen2.5:3b]
param(
    [string[]]$Models = @('qwen2.5:1.5b', 'gemma2:2b', 'qwen2.5:3b', 'qwen2.5:7b')
)

$ErrorActionPreference = 'Stop'

# With -File, "a,b" arrives as one string.
$Models = $Models | ForEach-Object { $_ -split ',' } | Where-Object { $_ }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Real students imported from the MON protocols (2025/2026 national rounds).
# Each surname below belongs to exactly one student in the database.
$cases = [ordered]@{
    # Nickname + surname.
    'Митко Митанкин'  = 'Димитър Петров Митанкин'
    'Гошо Генев'      = 'Георги Димитров Генев'
    'Сашо Наков'      = 'Александър Николаев Наков'
    'Ники Вълчанов'   = 'Николай Венелинов Вълчанов'
    'Ели Куцарова'    = 'Елена Светославова Куцарова'
    'Вики Ламбова'    = 'Виктория Людмилова Ламбова'
    'Боби Бъчваров'   = 'Борислав Росен Бъчваров'
    'Ицо Мисирков'    = 'Христо Боянов Мисирков'
    # Typos and Latin script.
    'Кирил Зулямки'        = 'Кирил Иванов Зулямски'
    'Aleksandar Bangachev' = 'Александър Атанасов Бангачев'
    'Petar Ovcharov'       = 'Петър Петров Овчаров'
    'Мария Богданва'       = 'Мария Гинова Богданова'
    'Радослав Хаджиев'     = 'Радослав Николаев Хаджиев'
    # Surname only.
    'Керацов'    = 'Калоян Христов Керацов'
    'Силгиджиян' = 'Александър Александров Силгиджиян'
    'Мисирков'   = 'Христо Боянов Мисирков'
    # Free-form sentences: common intros ("казвам се", "аз съм") are handled
    # without the model, so these use wording only the model understands.
    'как се е представил Кирил Зашев на олимпиадата?' = 'Кирил Красимиров Зашев'
    'колко точки има Гергана Рандева'                 = 'Гергана Миленова Рандева'
    'искам да видя постиженията на Виктория Димова'   = 'Виктория Красимирова Димова'
    'резултатът на съученичката ми Мария Богданова'   = 'Мария Гинова Богданова'
    'Кирил Зашев от десети клас'                      = 'Кирил Красимиров Зашев'
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
