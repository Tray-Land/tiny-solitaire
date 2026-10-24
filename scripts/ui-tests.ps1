param(
    [int]$AppPid = 0,
    [string]$TestFilter = '.*',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$ArtifactDirectory = (Join-Path $PSScriptRoot "..\artifacts\ui-tests\$([guid]::NewGuid().ToString('N'))")
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$project = Join-Path $PSScriptRoot '..\TinySolitaire.csproj'
$hwnd = $null

function Invoke-WinAppChecked([string[]]$Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $($output -join "`n")" }
    $output
}
function Invoke-Ui([string[]]$Arguments) {
    Invoke-WinAppChecked (@('ui') + $Arguments + @('-w', "$hwnd"))
}
function Test-UI([string]$Name, [scriptblock]$Script) {
    if ($Name -notmatch $TestFilter) { return }
    try {
        & $Script | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
    }
}
function Save-Screenshot([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    if (Test-Path -LiteralPath $path) { throw "Evidence already exists: $path" }
    Invoke-Ui @('screenshot', '--output', $path) | Out-Null
    if (!(Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw "Screenshot missing: $path" }
    $screenshots.Add($path)
}
function Select-Choice([string]$Control, [string]$Value) {
    Invoke-Ui @('invoke', $Control, '--action', 'expand') | Out-Null
    $tree = Invoke-Ui @('inspect', '--interactive', '--hide-offscreen', '--json') | ConvertFrom-Json
    $item = Read-Nodes $tree.windows.elements | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq $Value } | Select-Object -First 1
    if (!$item.selector) { throw "Dropdown item not found: $Value" }
    Invoke-Ui @('invoke', $item.selector, '--action', 'select') | Out-Null
    Invoke-Ui @('wait-for', $Control, '--value', $Value, '-t', '3000') | Out-Null
}
function Get-Text([string]$Control) {
    (Invoke-Ui @('get-value', $Control, '--json') | ConvertFrom-Json).text
}
function Read-Nodes($nodes) {
    foreach ($node in $nodes) {
        $node
        Read-Nodes $node.children
    }
}
function Read-State([int]$Moves = -1) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if (Test-Path -LiteralPath $savePath) {
            $state = (Get-Content -LiteralPath $savePath -Raw | ConvertFrom-Json).State
            if ($Moves -lt 0 -or $state.Moves -eq $Moves) { return $state }
        }
        Start-Sleep -Milliseconds 100
    } while ($watch.Elapsed.TotalSeconds -lt 4)
    throw "Saved state did not reach $Moves moves."
}
function Wait-Moves([int]$Moves) {
    Read-State $Moves | Out-Null
}
function Find-HintMove {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        Invoke-Ui @('invoke', 'ShowHint') | Out-Null
        $hint = Get-Text 'HintMessage'
        Invoke-Ui @('invoke', 'ShowHint') | Out-Null
        if ($hint -match '^Move ') {
            $tree = Invoke-Ui @('inspect', '--interactive', '--json') | ConvertFrom-Json
            $source = Read-Nodes $tree.windows.elements | Where-Object { $_.automationId -like 'Card_*' -and $_.name -like '*, selected' } | Select-Object -First 1
            if (!$source) { throw 'Hint did not select its source card.' }
            $state = Read-State
            if ($hint -match 'column (\d+)') {
                $column = [int]$Matches[1] - 1
                $target = if ($state.Tableau[$column].Count) { "Card_Tableau_${column}_$($state.Tableau[$column].Count - 1)" } else { "Column$column" }
            } else {
                $parts = $source.automationId -split '_'
                $card = if ($parts[1] -eq 'Waste') { $state.Waste[[int]$parts[3]] } else { $state.Tableau[[int]$parts[2]][[int]$parts[3]] }
                $foundation = 0..3 | Where-Object {
                    $pile = @($state.Foundations[$_])
                    ($pile.Count -eq 0 -and $card.Rank -eq 1) -or
                    ($pile.Count -gt 0 -and $pile[-1].Suit -eq $card.Suit -and $pile[-1].Rank + 1 -eq $card.Rank)
                } | Select-Object -First 1
                $count = $state.Foundations[$foundation].Count
                $target = if ($count) { "Card_Foundation_${foundation}_$($count - 1)" } else { "Foundation$foundation" }
            }
            $start = "$([int]($source.x + $source.width / 2)),$([int]($source.y + [Math]::Min(10, $source.height / 4)))"
            return @{ source = $source.automationId; start = $start; target = $target; moves = $state.Moves }
        }
        Invoke-Ui @('invoke', 'StockPile') | Out-Null
        Start-Sleep -Milliseconds 100
    }
    throw 'No hintable move found in one pass through the stock.'
}
function Find-TableauMove {
    for ($attempt = 0; $attempt -lt 25; $attempt++) {
        $state = Read-State
        $tree = Invoke-Ui @('inspect', '--interactive', '--hide-offscreen', '--json') | ConvertFrom-Json
        $nodes = @(Read-Nodes $tree.windows.elements)
        foreach ($source in $nodes | Where-Object { $_.automationId -like 'Card_*' }) {
            $parts = $source.automationId -split '_'
            $pile = [int]$parts[2]
            $index = [int]$parts[3]
            $card = switch ($parts[1]) {
                'Tableau' { $state.Tableau[$pile][$index] }
                'Foundation' { $state.Foundations[$pile][$index] }
                'Waste' { if ($index -eq $state.Waste.Count - 1) { $state.Waste[$index] } }
            }
            if (!$card) { continue }
            foreach ($column in 0..6) {
                if ($parts[1] -eq 'Tableau' -and $pile -eq $column) { continue }
                $onto = @($state.Tableau[$column])
                $legal = if ($onto.Count) {
                    $red = [int]$card.Suit -in @(1, 2)
                    $ontoRed = [int]$onto[-1].Suit -in @(1, 2)
                    $card.Rank -eq $onto[-1].Rank - 1 -and $red -ne $ontoRed
                } else { $card.Rank -eq 13 }
                if (!$legal) { continue }
                $targetId = if ($onto.Count) { "Card_Tableau_${column}_$($onto.Count - 1)" } else { "Column$column" }
                $target = $nodes | Where-Object automationId -eq $targetId | Select-Object -First 1
                if (!$target) { continue }
                $start = "$([int]($source.x + $source.width / 2)),$([int]($source.y + [Math]::Min(10, $source.height / 4)))"
                return @{ start = $start; target = $target; moves = $state.Moves; column = $column; count = $onto.Count }
            }
        }
        Invoke-Ui @('invoke', 'StockPile') | Out-Null
        Wait-Moves ($state.Moves + 1)
    }
    throw 'No legal tableau move found in one pass through the stock.'
}

try {
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
    if ($AppPid -eq 0) {
        $launch = Invoke-WinAppChecked @('run', $project, '-c', $Configuration, '--detach', '--json') | ConvertFrom-Json
        $AppPid = [int]$launch.ProcessId
        if ($AppPid -le 0) { throw 'Launch returned no process ID.' }
    }
    # Tray apps use their single-instance handoff to make the flyout visible.
    Invoke-WinAppChecked @('run', $project, '-c', $Configuration, '--no-build', '--detach', '--json') | Out-Null
    Start-Sleep -Milliseconds 500
    $windows = @(Invoke-WinAppChecked @('ui', 'list-windows', '-a', 'TinySolitaire', '--json') | ConvertFrom-Json)
    $main = $windows | Where-Object { $_.title -eq 'Tiny Solitaire' } | Select-Object -First 1
    if (!$main) { throw 'No responsive Solitaire flyout found.' }
    $hwnd = $main.hwnd
    $AppPid = $main.processId
    $manifest = [xml](Get-Content (Join-Path $PSScriptRoot '..\Package.appxmanifest') -Raw)
    $family = (Get-AppxPackage -Name $manifest.Package.Identity.Name).PackageFamilyName
    if (!$family) { throw 'Development package registration not found.' }
    $savePath = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalState\game.json"

    Test-UI 'Responsive game and initial screenshot' {
        Invoke-Ui @('wait-for', 'StockPile', '-t', '3000')
        foreach ($label in @('HintText', 'DealLabel', 'ScoreText', 'MovesText', 'TimerText')) {
            Invoke-Ui @('wait-for', $label, '--gone', '-t', '1000')
        }
        Invoke-Ui @('wait-for', 'HintMessage', '--gone', '-t', '1000')
        Save-Screenshot '01-initial.png'
    }
    Test-UI 'Hints appear only when requested' {
        Invoke-Ui @('invoke', 'ShowHint')
        Invoke-Ui @('wait-for', 'HintMessage', '-t', '3000')
        if (!(Get-Text 'HintMessage')) { throw 'Requested hint has no message.' }
        Save-Screenshot '07-requested-hint.png'
        Invoke-Ui @('invoke', 'ShowHint')
        Invoke-Ui @('wait-for', 'HintMessage', '--gone', '-t', '3000')
        Save-Screenshot '08-clean-board.png'
    }
    Test-UI 'Stock draws and undo restores the deal' {
        $before = (Read-State).Moves
        Invoke-Ui @('invoke', 'StockPile')
        Wait-Moves ($before + 1)
        Invoke-Ui @('invoke', 'UndoMove')
        Wait-Moves $before
    }
    Test-UI 'Hint selects a legal card; click-to-move and undo work' {
        $move = Find-HintMove
        Invoke-Ui @('invoke', $move.target)
        Wait-Moves ($move.moves + 1)
        Invoke-Ui @('invoke', 'UndoMove')
        Wait-Moves $move.moves
    }
    Test-UI 'Drag-and-drop performs the hinted move' {
        $move = Find-HintMove
        Invoke-Ui @('drag', $move.start, $move.target, '--hold-ms', '200', '--dwell-ms', '250')
        Wait-Moves ($move.moves + 1)
        Invoke-Ui @('invoke', 'UndoMove')
        Wait-Moves $move.moves
    }
    Test-UI 'Cards and pile slots do not show tooltips' {
        $tree = Invoke-Ui @('inspect', '--interactive', '--json') | ConvertFrom-Json
        $nodes = @(Read-Nodes $tree.windows.elements)
        $card = $nodes | Where-Object { $_.automationId -like 'Card_*' } | Select-Object -First 1
        $foundation = $nodes | Where-Object { $_.automationId -match '^(Foundation\d|Card_Foundation_)' } | Select-Object -First 1
        if (!$card -or !$foundation) { throw 'Expected cards and foundation slots to hover.' }
        foreach ($selector in @($card.automationId, $foundation.automationId, 'StockPile')) {
            Invoke-Ui @('hover', $selector, '--dwell-time', '1500')
            $hovered = Invoke-WinAppChecked @('ui', 'inspect', '-a', "$AppPid", '--depth', '12', '--json') | ConvertFrom-Json
            $tooltips = @(Read-Nodes $hovered.windows.elements | Where-Object { $_.type -eq 'ToolTip' -and !$_.isOffscreen })
            if ($tooltips.Count) { throw "Unexpected tooltip on ${selector}: $($tooltips.name -join ', ')" }
        }
        Save-Screenshot '10-stock-without-tooltip.png'
    }
    Test-UI 'Drag accepts card overlap when the cursor misses the pile' {
        $move = Find-HintMove
        $tree = Invoke-Ui @('inspect', '--interactive', '--json') | ConvertFrom-Json
        $target = Read-Nodes $tree.windows.elements | Where-Object automationId -eq $move.target | Select-Object -First 1
        if (!$target) { throw 'Hint destination is missing.' }
        $scale = $target.width / 48
        if ($move.target -match 'Foundation') {
            $end = "$([int]($target.x + $target.width / 2)),$([int]($target.y - 8 * $scale))"
        } else {
            $end = "$([int]($target.x + $target.width + 2 * $scale)),$([int]($target.y + 10 * $scale))"
        }
        Invoke-Ui @('drag', $move.start, $end, '--hold-ms', '200', '--dwell-ms', '250')
        Wait-Moves ($move.moves + 1)
        Invoke-Ui @('invoke', 'UndoMove')
        Wait-Moves $move.moves
    }
    Test-UI 'Drag accepts blank space below a tableau stack' {
        $move = Find-TableauMove
        $target = $move.target
        $scale = $target.width / 48
        $end = "$([int]($target.x + $target.width / 2)),$([int]($target.y + $target.height + 24 * $scale))"
        Invoke-Ui @('drag', $move.start, $end, '--hold-ms', '200', '--dwell-ms', '250')
        $after = Read-State ($move.moves + 1)
        if ($after.Tableau[$move.column].Count -le $move.count) { throw 'Drop did not land in the intended tableau column.' }
        Save-Screenshot '11-forgiving-tableau-drop.png'
        Invoke-Ui @('invoke', 'UndoMove')
        Wait-Moves $move.moves
    }
    Test-UI 'New deal asks before replacing progress; cancel preserves it' {
        $moves = (Read-State).Moves
        Invoke-Ui @('invoke', 'StockPile')
        Wait-Moves ($moves + 1)
        $before = Read-State ($moves + 1)
        Invoke-Ui @('invoke', 'NewGame')
        Invoke-Ui @('wait-for', 'Primary', '-t', '3000')
        Invoke-Ui @('invoke', 'CloseButton')
        Wait-Moves $before.Moves
    }
    Test-UI 'Double-click sends a waste ace to a foundation' {
        $state = Read-State
        for ($attempt = 0; $attempt -lt 25 -and ($state.Waste.Count -eq 0 -or $state.Waste[-1].Rank -ne 1); $attempt++) {
            Invoke-Ui @('invoke', 'StockPile')
            Wait-Moves ($state.Moves + 1)
            $state = Read-State
        }
        if (!$state.Waste.Count -or $state.Waste[-1].Rank -ne 1) { throw 'No ace appeared in a pass through the stock.' }
        $before = $state.Moves
        Invoke-Ui @('click', "Card_Waste_0_$($state.Waste.Count - 1)", '--double')
        Wait-Moves ($before + 1)
        $after = Read-State
        if (($after.Foundations | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum -ne 1) {
            throw 'Double-click did not build a foundation.'
        }
    }
    Test-UI 'Green appearance keeps the game surface text-free' {
        $before = Read-State | Select-Object Stock, Waste, Foundations, Tableau, DrawCount, Moves, Score, Redeals | ConvertTo-Json -Depth 8 -Compress
        foreach ($choice in @('Light', 'Dark', 'System', 'Green')) {
            Invoke-Ui @('invoke', 'OpenSettings')
            Select-Choice 'ThemeChoice' $choice
            Invoke-Ui @('invoke', 'BackToGame')
            Invoke-Ui @('wait-for', 'StockPile', '-t', '3000')
        }
        $tree = Invoke-Ui @('inspect', '--depth', '12', '--hide-offscreen', '--json') | ConvertFrom-Json
        $clutter = @(Read-Nodes $tree.windows.elements | Where-Object {
            $_.type -eq 'Text' -and $_.name -in @('Tiny Solitaire', 'A little moment of play.', 'Waste', 'New', 'Undo', 'Hint', 'Settings', 'Hide to tray')
        })
        if ($clutter.Count) { throw "Unexpected visible game text: $($clutter.name -join ', ')" }
        foreach ($command in @('NewGame', 'UndoMove', 'ShowHint', 'OpenSettings', 'HideGame')) {
            Invoke-Ui @('wait-for', $command, '-t', '1000')
        }
        $after = Read-State | Select-Object Stock, Waste, Foundations, Tableau, DrawCount, Moves, Score, Redeals | ConvertTo-Json -Depth 8 -Compress
        if ($before -ne $after) { throw 'Appearance changes modified the current deal.' }
        Save-Screenshot '09-green-surface.png'
    }
    Test-UI 'All emoji backs and appearance choices are available' {
        Invoke-Ui @('invoke', 'OpenSettings')
        foreach ($choice in @('Stargazer', 'Fruit salad', 'Cat nap', 'Emoji garden')) {
            Select-Choice 'CardChoice' $choice
        }
        Select-Choice 'ThemeChoice' 'Dark'
        Save-Screenshot '02-settings-dark.png'
        Invoke-Ui @('invoke', 'BackToGame')
        Save-Screenshot '03-game-dark.png'
        Invoke-Ui @('invoke', 'OpenSettings')
        Select-Choice 'ThemeChoice' 'Light'
        Invoke-Ui @('invoke', 'BackToGame')
        Save-Screenshot '04-game-light.png'
        Invoke-Ui @('invoke', 'OpenSettings')
        Select-Choice 'ThemeChoice' 'System'
        Select-Choice 'ThemeChoice' 'Green'
        Invoke-Ui @('invoke', 'BackToGame')
    }
    Test-UI 'Draw preference preserves current deal and applies to a new one' {
        Invoke-Ui @('invoke', 'OpenSettings')
        $before = Read-State
        Select-Choice 'DrawChoice' 'Draw three'
        Invoke-Ui @('invoke', 'BackToGame')
        $same = Read-State
        if ($before.DrawCount -ne $same.DrawCount) { throw 'Draw choice changed an active deal.' }
        Invoke-Ui @('invoke', 'NewGame')
        Invoke-Ui @('wait-for', 'Primary', '-t', '3000')
        Invoke-Ui @('invoke', 'Primary')
        Wait-Moves 0
        Invoke-Ui @('invoke', 'StockPile')
        Wait-Moves 1
        $state = Read-State 1
        if ($state.DrawCount -ne 3 -or $state.Waste.Count -ne 3 -or $state.Stock.Count -ne 21) {
            throw 'Draw-three packet was incorrect.'
        }
        Save-Screenshot '05-draw-three.png'
    }
    Test-UI 'Escape returns from settings and keyboard undo works' {
        Invoke-Ui @('invoke', 'OpenSettings')
        Invoke-Ui @('send-keys', 'esc', '--via', 'send-input')
        Invoke-Ui @('wait-for', 'StockPile', '-t', '3000')
        Invoke-Ui @('send-keys', 'ctrl+z', '--via', 'send-input')
        Wait-Moves 0
    }
    Test-UI 'App controls have accessible names and stable IDs' {
        $tree = Invoke-Ui @('inspect', '--interactive', '--hide-offscreen', '--json') | ConvertFrom-Json
        $elements = @(Read-Nodes $tree.windows.elements | Where-Object { $_.type -match 'Button|ComboBox|ToggleSwitch' })
        if ($elements.Count -lt 10) { throw 'No meaningful game UI tree was audited.' }
        $missing = @($elements | Where-Object { !$_.automationId -or !$_.name })
        if ($missing.Count) { throw "Unnamed or unidentified controls: $($missing | ConvertTo-Json -Compress)" }
        Save-Screenshot '06-final.png'
    }
} catch {
    $results.Add(@{ name = 'Target setup / execution'; status = 'FAIL'; detail = "$_" })
} finally {
    try { Invoke-WinAppChecked @('ui', 'yield') | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
}

$failed = @($results | Where-Object status -eq 'FAIL').Count
$report = [ordered]@{
    target = 'local'; appPid = $AppPid; passed = $results.Count - $failed; failed = $failed
    results = @($results.ToArray()); screenshots = @($screenshots.ToArray())
}
$reportPath = Join-Path $ArtifactDirectory 'test-results.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8
$results | ForEach-Object { Write-Host "$($_.status): $($_.name) $(if ($_.detail) { $_.detail })" }
Write-Host "Passed: $($report.passed) | Failed: $failed | Results: $reportPath"
if ($failed) { exit 1 }
