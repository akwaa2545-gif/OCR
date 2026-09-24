$ErrorActionPreference = 'Stop'
if ($env:COMPUTERNAME -ne 'THBTCDT-CM1XKG2') { throw 'Unexpected host.' }
if ([Security.Principal.WindowsIdentity]::GetCurrent().Name -ine 'KEMET\2172172205529') { throw 'Unexpected account.' }
$report = [ordered]@{ CompletedUtc = $null; Roots = @(); FreeBytes = (Get-PSDrive C).Free; Error = $null }
try {
    foreach ($root in @('\\svr120a\Cert$', 'C:\uploads')) {
        $item = Get-Item -LiteralPath $root -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unsafe root.' }
        $entry = [ordered]@{ Root = $root; Writable = $false; AtomicMove = $false; Files = 0; PdfFiles = 0; Bytes = [long]0; LargestPdfBytes = [long]0; Directories = 0; ReparseSkipped = 0; Complete = $true; Extensions = @{} }
        $probe = Join-Path $root ('.ocr-cert-probe-' + [guid]::NewGuid().ToString('N') + '.tmp')
        $moved = $probe + '.moved'
        $created = $false
        $moveCompleted = $false
        try {
            $stream = [IO.File]::Open($probe, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            $created = $true
            try { $stream.WriteByte(42); $stream.Flush() } finally { $stream.Dispose() }
            $entry.Writable = $true
            [IO.File]::Move($probe, $moved)
            $moveCompleted = $true
            $entry.AtomicMove = ([IO.File]::ReadAllBytes($moved)[0] -eq 42)
        } finally {
            if ($created) {
                $owned = @($probe)
                if ($moveCompleted) { $owned += $moved }
                foreach ($file in $owned) {
                    if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
                }
            }
        }
        $queue = New-Object 'Collections.Generic.Queue[string]'
        $queue.Enqueue($root)
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($queue.Count -gt 0) {
            if ($timer.Elapsed.TotalSeconds -ge 100 -or $entry.Files -ge 50000) { $entry.Complete = $false; break }
            $directory = $queue.Dequeue()
            foreach ($child in Get-ChildItem -LiteralPath $directory -Force) {
                if ($child.Attributes -band [IO.FileAttributes]::ReparsePoint) { $entry.ReparseSkipped++; continue }
                if ($child.PSIsContainer) { $entry.Directories++; $queue.Enqueue($child.FullName); continue }
                $entry.Files++
                $entry.Bytes += $child.Length
                $extension = $child.Extension.ToLowerInvariant()
                if (!$entry.Extensions.ContainsKey($extension)) { $entry.Extensions[$extension] = 0 }
                $entry.Extensions[$extension]++
                if ($extension -eq '.pdf') {
                    $entry.PdfFiles++
                    $entry.LargestPdfBytes = [Math]::Max($entry.LargestPdfBytes, $child.Length)
                }
            }
        }
        $report.Roots += $entry
    }
} catch { $report.Error = $_.Exception.GetType().Name }
$report.CompletedUtc = [DateTime]::UtcNow.ToString('o')
[IO.File]::WriteAllText('C:\ocr-deploy\certificate-access.json', ($report | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
if ($report.Error) { exit 1 }
