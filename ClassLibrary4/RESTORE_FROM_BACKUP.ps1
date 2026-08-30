param(
    [Parameter(Mandatory=$true)]
    [string]$BackupFolder
)

$ErrorActionPreference = "Stop"

function Resolve-SourceRoot {
    $here = (Get-Location).Path
    if (Test-Path (Join-Path $here "ClassLibrary4\ClassLibrary4.csproj")) {
        return (Join-Path $here "ClassLibrary4")
    }
    if (Test-Path (Join-Path $here "ClassLibrary4.csproj")) {
        return $here
    }
    throw "Không tìm thấy ClassLibrary4.csproj."
}

$src = Resolve-SourceRoot
if (-not (Test-Path $BackupFolder)) {
    throw "Backup không tồn tại: $BackupFolder"
}

Get-ChildItem $BackupFolder -File | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $src $_.Name) -Force
    Write-Host "[RESTORE] $($_.Name)"
}

Write-Host "Đã khôi phục source từ backup. Rebuild Solution lại trước khi NETLOAD."
