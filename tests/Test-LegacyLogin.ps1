param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::SetUnhandledExceptionMode(
    [System.Windows.Forms.UnhandledExceptionMode]::ThrowException)
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $ExecutablePath).Path)
$form = [Activator]::CreateInstance($assembly.GetType('OperatorTrainingRecord.Login', $true))
try {
    # Exercise the real Load event without showing a window or attempting login.
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Location = New-Object System.Drawing.Point(-10000, -10000)
    $form.ShowInTaskbar = $false
    $form.Opacity = 0
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    foreach ($name in @('txtUser', 'txtPassword')) {
        $matches = $form.Controls.Find($name, $true)
        if ($matches.Count -ne 1) { throw "Missing login input: $name" }
        $inputControl = $matches[0]
        if (-not $inputControl.Visible -or -not $inputControl.Enabled -or -not $inputControl.CanSelect) {
            throw "Login input is not usable: $name"
        }
        if (-not $inputControl.Parent.ClientRectangle.Contains($inputControl.Bounds)) {
            throw "Login input is clipped: $name"
        }
        $panel = $inputControl.Parent
        if (-not $panel.Parent.ClientRectangle.Contains($panel.Bounds)) {
            throw "Login panel is clipped: $name"
        }
        $inputControl.Text = 'test'
        $form.ActiveControl = $inputControl
        if (-not $inputControl.ContainsFocus) { throw "Cannot focus login input: $name" }
        $inputControl.Clear()
    }
    $password = $form.Controls.Find('txtPassword', $true)[0]
    if (-not $password.UseSystemPasswordChar) { throw 'Password must be masked.' }
    Write-Output 'PASS: Login loads; username/password are visible, enabled, unclipped, editable, focusable; password is masked.'
} finally {
    $form.Close()
    $form.Dispose()
}
