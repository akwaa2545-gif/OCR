function Invoke-OcrSecretStdinProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FileName,
        [AllowEmptyString()][string]$Arguments,
        [Parameter(Mandatory)][AllowEmptyString()][string]$InputText,
        [ValidateRange(1, 120)][int]$TimeoutSeconds = 45
    )
    # GitHub tokens are small. Bound the write to avoid blocking on a full pipe.
    if ([Text.Encoding]::UTF8.GetByteCount($InputText) -gt 2048) { throw 'Registry input exceeds the supported size.' }
    $process = New-Object Diagnostics.Process
    $timedOut = $false
    $started = $false
    try {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = $FileName
        $start.Arguments = $Arguments
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardInput = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.EnvironmentVariables.Remove('GH_TOKEN')
        $process.StartInfo = $start
        if (-not $process.Start()) { throw 'Start failed.' }
        $started = $true
        # Drain both streams concurrently before writing or waiting.
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        # .NET Framework/PS5.1 lacks ProcessStartInfo.StandardInputEncoding.
        # Write UTF8 bytes directly to avoid the PowerShell pipeline and BOMs.
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($InputText)
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.BaseStream.Flush()
        $process.StandardInput.Close()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $timedOut = $true
            $process.Kill()
            $null = $process.WaitForExit(5000)
            throw 'Timeout.'
        }
        if (-not [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]@($stdout, $stderr), 5000)) { throw 'Stream drain timeout.' }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdout.Result
            StandardError = $stderr.Result
        }
    } catch {
        if ($timedOut) { throw 'Registry client process timed out.' }
        throw 'Registry client process could not complete.'
    } finally {
        if ($started -and -not $process.HasExited) {
            try { $process.Kill() } catch { Write-Warning 'Registry client cleanup did not terminate the process.' }
        }
        $process.Dispose()
    }
}

function Get-OcrGitHubDiagnosticStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Token,
        [ValidateSet('Repository', 'Package')][string]$Resource = 'Repository'
    )
    # Fixed allowlist: never send authorization to a user-controlled URL.
    $url = 'https://api.github.com/repos/akwaa2545-gif/OCR'
    if ($Resource -eq 'Package') { $url = 'https://api.github.com/users/akwaa2545-gif/packages/container/ocr%2Focr-web' }
    $response = $null
    try {
        $request = [Net.HttpWebRequest]::Create($url)
        $request.Method = 'GET'
        $request.AllowAutoRedirect = $false
        $request.Timeout = 10000
        $request.ReadWriteTimeout = 10000
        $request.UserAgent = 'OCR-deployment-diagnostic'
        $request.Accept = 'application/vnd.github+json'
        $request.Headers['Authorization'] = 'Bearer ' + $Token
        $request.Headers['X-GitHub-Api-Version'] = '2022-11-28'
        $response = $request.GetResponse()
        return [int]$response.StatusCode
    } catch [Net.WebException] {
        if ($null -ne $_.Exception.Response) {
            $response = $_.Exception.Response
            return [int]$response.StatusCode
        }
        return 0
    } catch {
        return 0
    } finally {
        if ($null -ne $response) { $response.Dispose() }
    }
}
