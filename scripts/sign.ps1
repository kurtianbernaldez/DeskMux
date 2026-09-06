param(
    [Parameter(Mandatory)][string[]]$Path,
    [Parameter(Mandatory)][string]$CertificatePath,
    [Parameter(Mandatory)][string]$CertificatePassword
)
$ErrorActionPreference = 'Stop'
$kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$signTool = Get-ChildItem -LiteralPath $kits -Filter signtool.exe -Recurse |
    Where-Object FullName -Match '\\x64\\signtool\.exe$' |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool) { throw 'Windows SDK signtool.exe was not found.' }
foreach ($item in $Path) {
    & $signTool.FullName sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $CertificatePath /p $CertificatePassword $item
    if ($LASTEXITCODE -ne 0) { throw "Signing failed for $item" }
}
