param(
    [switch]$SkipBuild,
    [switch]$CleanupAfterSuccessfulBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-SourceRoot {
    $here = (Get-Location).Path
    if (Test-Path (Join-Path $here "ClassLibrary4\ClassLibrary4.csproj")) {
        return (Join-Path $here "ClassLibrary4")
    }
    if (Test-Path (Join-Path $here "ClassLibrary4.csproj")) {
        return $here
    }
    throw "Không tìm thấy ClassLibrary4.csproj. Hãy chạy script tại thư mục repo hoặc thư mục ClassLibrary4."
}

function Read-Utf8([string]$Path) {
    $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    return $text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Write-Utf8([string]$Path, [string]$Text) {
    [System.IO.File]::WriteAllText(
        $Path,
        $Text,
        [System.Text.UTF8Encoding]::new($false))
}

function Normalize-Block([string]$Value) {
    if ($null -eq $Value) { return "" }
    return $Value.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Replace-Required(
    [string]$Path,
    [string]$Old,
    [string]$New,
    [string]$Label,
    [int]$ExpectedMin = 1)
{
    $text = Normalize-Block (Read-Utf8 $Path)
    $oldNorm = Normalize-Block $Old
    $newNorm = Normalize-Block $New

    $count = 0
    $start = 0
    while ($true) {
        $i = $text.IndexOf($oldNorm, $start, [System.StringComparison]::Ordinal)
        if ($i -lt 0) { break }
        $count++
        $start = $i + $oldNorm.Length
    }

    if ($count -lt $ExpectedMin) {
        if ($text.Contains($newNorm)) {
            Write-Host "[SKIP] $Label đã được áp dụng."
            return
        }

        throw "Không tìm thấy neo patch '$Label' trong $Path. Không sửa tiếp để tránh phá code."
    }

    $text = $text.Replace($oldNorm, $newNorm)
    Write-Utf8 $Path $text
    Write-Host "[OK] $Label ($count vị trí)"
}

function Replace-First-Required(
    [string]$Path,
    [string]$Old,
    [string]$New,
    [string]$Label)
{
    $text = Normalize-Block (Read-Utf8 $Path)
    $oldNorm = Normalize-Block $Old
    $newNorm = Normalize-Block $New

    $i = $text.IndexOf($oldNorm, [System.StringComparison]::Ordinal)
    if ($i -lt 0) {
        if ($text.Contains($newNorm)) {
            Write-Host "[SKIP] $Label đã được áp dụng."
            return
        }

        throw "Không tìm thấy neo patch '$Label' trong $Path. Không sửa tiếp để tránh phá code."
    }

    $newText = $text.Substring(0, $i) + $newNorm + $text.Substring($i + $oldNorm.Length)
    Write-Utf8 $Path $newText
    Write-Host "[OK] $Label"
}

function Add-After-Required(
    [string]$Path,
    [string]$Anchor,
    [string]$Insert,
    [string]$Label)
{
    $text = Normalize-Block (Read-Utf8 $Path)
    $anchorNorm = Normalize-Block $Anchor
    $insertNorm = Normalize-Block $Insert

    if ($text.Contains($insertNorm.Trim())) {
        Write-Host "[SKIP] $Label đã có."
        return
    }

    $i = $text.IndexOf($anchorNorm, [System.StringComparison]::Ordinal)
    if ($i -lt 0) {
        throw "Không tìm thấy neo patch '$Label' trong $Path."
    }

    $newText = $text.Substring(0, $i + $anchorNorm.Length) + $insertNorm + $text.Substring($i + $anchorNorm.Length)
    Write-Utf8 $Path $newText
    Write-Host "[OK] $Label"
}

$src = Resolve-SourceRoot
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$payload = Join-Path $packageRoot "files"

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backup = Join-Path (Split-Path -Parent $src) ("_backup_7_improvements_" + $stamp)
New-Item -ItemType Directory -Force -Path $backup | Out-Null

$targets = @(
    "MepSymbolClassifier.cs",
    "MepYoloSymbolDetector.cs",
    "MepGraphGnnClassifier.cs",
    "BOCTACHUI.HvacAi.cs",
    "BOCTACHUI.FireDesign.cs",
    "BOCTACHUI.FireDrawingReader.cs",
    "BOCTACHUI.FireHydraulicDesign.cs",
    "MepArchitectureSelfTests.cs"
)

foreach ($name in $targets) {
    $p = Join-Path $src $name
    if (Test-Path $p) {
        Copy-Item $p (Join-Path $backup $name) -Force
    }
}

foreach ($name in @("MepRuntimeIntegration.cs","MepCloudServices.cs")) {
    $dest = Join-Path $src $name
    if (Test-Path $dest) {
        Copy-Item $dest (Join-Path $backup $name) -Force
    }
    Copy-Item (Join-Path $payload $name) $dest -Force
}
Write-Host "[OK] Thêm runtime integration + cloud service boundaries."

# 1) ENGINE RUN DIAGNOSTICS - mark only actual inference calls.
$onnxPath = Join-Path $src "MepSymbolClassifier.cs"
$onnxOld = @'
                    using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                        _session.Run(inputs))
'@
$onnxNew = @'
                    MepAiRuntimeDiagnostics.MarkRun("ONNX");

                    using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                        _session.Run(inputs))
'@
Replace-Required $onnxPath $onnxOld $onnxNew "ONNX actual RUNS counter"

$yoloPath = Join-Path $src "MepYoloSymbolDetector.cs"
$yoloOld = @'
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs);
'@
$yoloNew = @'
                MepAiRuntimeDiagnostics.MarkRun("YOLO");

                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs);
'@
Replace-Required $yoloPath $yoloOld $yoloNew "YOLO actual RUNS counter"

$gnnPath = Join-Path $src "MepGraphGnnClassifier.cs"
$gnnOld = @'
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs))
'@
$gnnNew = @'
                MepAiRuntimeDiagnostics.MarkRun("GNN");

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    _session.Run(inputs))
'@
Replace-Required $gnnPath $gnnOld $gnnNew "GNN actual RUNS counter"

# 2) SHARED SCAN SNAPSHOT: capture the single HVAC/MEP scan selection.
$hvacPath = Join-Path $src "BOCTACHUI.HvacAi.cs"
$hvacAnchor = @'
                SelectionSet workingSelection =
                    psr.Value;
'@
$hvacReplacement = @'
                SelectionSet workingSelection =
                    psr.Value;

                MepScanSessionStore.CaptureSelection(
                    doc,
                    workingSelection.GetObjectIds(),
                    new[] { "MEP", "PIPE", "DUCT", "HVAC", "DEVICE" });
'@
Replace-Required $hvacPath $hvacAnchor $hvacReplacement "HVAC shared scan snapshot capture"

# 3) PCCC AREA: prefer fresh shared snapshot, fall back to the old prompt.
$fireAreaPath = Join-Path $src "BOCTACHUI.FireDesign.cs"
$areaOld = @'
                    PromptSelectionResult selection =
                        ed.GetSelection(
                            options,
                            new SelectionFilter(values));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null ||
                        selection.Value.Count == 0)
                    {
                        SetFireDesignStatus(
                            "Đã hủy quét hoặc chưa chọn được vùng nào.",
                            isError: false);
                        return;
                    }
'@
$areaNew = @'
                    SelectionSet fireSelection = null;

                    if (!MepScanSessionStore.TryGetFreshSelection(
                            doc,
                            out fireSelection))
                    {
                        PromptSelectionResult selection =
                            ed.GetSelection(
                                options,
                                new SelectionFilter(values));

                        if (selection.Status != PromptStatus.OK ||
                            selection.Value == null ||
                            selection.Value.Count == 0)
                        {
                            SetFireDesignStatus(
                                "Đã hủy quét hoặc chưa chọn được vùng nào.",
                                isError: false);
                            return;
                        }

                        fireSelection = selection.Value;
                        MepScanSessionStore.CaptureSelection(
                            doc,
                            fireSelection.GetObjectIds(),
                            new[] { "PCCC", "AREA" });
                    }
'@
Replace-Required $fireAreaPath $areaOld $areaNew "PCCC area consumes shared snapshot"
Replace-First-Required $fireAreaPath `
    '                        foreach (SelectedObject selected in selection.Value)' `
    '                        foreach (SelectedObject selected in fireSelection)' `
    "PCCC area iterates snapshot selection"

# 4) PCCC TEXT/BLOCK: reuse the same scan snapshot.
$fireTextPath = Join-Path $src "BOCTACHUI.FireDrawingReader.cs"
$textOld = @'
                    PromptSelectionResult selection =
                        ed.GetSelection(
                            options,
                            new SelectionFilter(values));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null ||
                        selection.Value.Count == 0)
                    {
                        SetFireDesignStatus(
                            "Đã hủy quét hoặc vùng chọn không có chữ/block.",
                            isError: false);
                        return;
                    }
'@
$textNew = @'
                    SelectionSet fireTextSelection = null;

                    if (!MepScanSessionStore.TryGetFreshSelection(
                            doc,
                            out fireTextSelection))
                    {
                        PromptSelectionResult selection =
                            ed.GetSelection(
                                options,
                                new SelectionFilter(values));

                        if (selection.Status != PromptStatus.OK ||
                            selection.Value == null ||
                            selection.Value.Count == 0)
                        {
                            SetFireDesignStatus(
                                "Đã hủy quét hoặc vùng chọn không có chữ/block.",
                                isError: false);
                            return;
                        }

                        fireTextSelection = selection.Value;
                        MepScanSessionStore.CaptureSelection(
                            doc,
                            fireTextSelection.GetObjectIds(),
                            new[] { "PCCC", "TEXT", "BLOCK" });
                    }
'@
Replace-Required $fireTextPath $textOld $textNew "PCCC text/block consumes shared snapshot"
Replace-First-Required $fireTextPath `
    '                        foreach (SelectedObject selected in selection.Value)' `
    '                        foreach (SelectedObject selected in fireTextSelection)' `
    "PCCC text/block iterates snapshot selection"

# 5) PCCC hydraulic critical path: reuse fresh graph/MEP selection when available.
# It still falls back to manual selection. This removes repeated segment selection
# when the current MEP snapshot already represents the working route/region.
$hydPath = Join-Path $src "BOCTACHUI.FireHydraulicDesign.cs"
$hydOld = @'
                    PromptSelectionResult selection =
                        doc.Editor.GetSelection(
                            options,
                            new SelectionFilter(filterValues));

                    if (selection.Status != PromptStatus.OK ||
                        selection.Value == null ||
                        selection.Value.Count == 0)
                    {
                        SetFireDesignStatus(
                            "Đã hủy hoặc chưa chọn tuyến ống bất lợi.",
                            isError: false);
                        return;
                    }
'@
$hydNew = @'
                    SelectionSet hydraulicSelection = null;

                    if (!MepScanSessionStore.TryGetFreshSelection(
                            doc,
                            out hydraulicSelection))
                    {
                        PromptSelectionResult selection =
                            doc.Editor.GetSelection(
                                options,
                                new SelectionFilter(filterValues));

                        if (selection.Status != PromptStatus.OK ||
                            selection.Value == null ||
                            selection.Value.Count == 0)
                        {
                            SetFireDesignStatus(
                                "Đã hủy hoặc chưa chọn tuyến ống bất lợi.",
                                isError: false);
                            return;
                        }

                        hydraulicSelection = selection.Value;
                    }
'@
Replace-Required $hydPath $hydOld $hydNew "PCCC hydraulic reuses shared snapshot"
Replace-First-Required $hydPath `
    '                        foreach (SelectedObject selected in selection.Value)' `
    '                        foreach (SelectedObject selected in hydraulicSelection)' `
    "PCCC hydraulic iterates snapshot selection"

# 6) Tests: make the existing architecture RunAll include runtime integration checks.
$testPath = Join-Path $src "MepArchitectureSelfTests.cs"
$testAnchor = '            TestSnapshotFingerprintStable();'
$testInsert = @'

            MepRuntimeIntegrationSelfTests.RunAll();
'@
Add-After-Required $testPath $testAnchor $testInsert "Runtime architecture self-tests"

Write-Host ""
Write-Host "============================================================"
Write-Host "PATCH XONG. Backup: $backup"
Write-Host "============================================================"

if (-not $SkipBuild) {
    Write-Host "Đang build Release x64..."
    Push-Location $src
    try {
        dotnet restore ".\ClassLibrary4.csproj"
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore thất bại." }

        dotnet build ".\ClassLibrary4.csproj" -c Release -p:Platform=x64 --no-restore
        if ($LASTEXITCODE -ne 0) { throw "Build thất bại. Source backup vẫn còn tại: $backup" }

        Write-Host "[OK] BUILD RELEASE x64 THÀNH CÔNG."
    }
    finally {
        Pop-Location
    }
}

if ($CleanupAfterSuccessfulBuild -and -not $SkipBuild) {
    # Safe cleanup only. DO NOT delete any V25 recovery DLL or native runtime here.
    $cleanup = @(
        (Join-Path $src "Class1.cs")
    )

    Get-ChildItem $src -Filter "*.bak" -File -ErrorAction SilentlyContinue |
        ForEach-Object { $cleanup += $_.FullName }

    $archive = Join-Path $backup "_safe_cleanup_archived"
    New-Item -ItemType Directory -Force -Path $archive | Out-Null

    foreach ($item in ($cleanup | Select-Object -Unique)) {
        if (Test-Path $item) {
            Move-Item $item $archive -Force
            Write-Host "[ARCHIVE] $item"
        }
    }

    Write-Host "Không xóa ClassLibrary4.V25*.dll, ONNX/OpenCV native DLL hoặc file recovery."
}

Write-Host ""
Write-Host "Xong. Mở AutoCAD lại hoàn toàn trước khi NETLOAD DLL mới."
