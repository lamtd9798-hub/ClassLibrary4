param(
    [string]$ProjectFolder = "."
)

$ErrorActionPreference = "Stop"

$xaml = Join-Path $ProjectFolder "BOCTACHUI.xaml"

if (-not (Test-Path $xaml)) {
    throw "Không tìm thấy BOCTACHUI.xaml tại: $xaml"
}

$backup = "$xaml.step30bd.bak"

if (-not (Test-Path $backup)) {
    Copy-Item $xaml $backup -Force
    Write-Host "Đã backup: $backup"
}

$text = Get-Content $xaml -Raw -Encoding UTF8

$replacements = @(
    @('Click="BtnAiAutoTakeoff_Click"', 'Click="BtnAiAutoTakeoffHvac_Click"'),
    @('Click="BtnAiAutoRun_Click"', 'Click="BtnAiAutoRunHvac_Click"'),
    @('Click="BtnAiDeviceOnly_Click"', 'Click="BtnAiDeviceOnlyHvac_Click"'),
    @('Content="AI TỰ ĐỘNG: ỐNG + VAN/TB"', 'Content="AI TỰ ĐỘNG: ỐNG + OG + VAN/TB"'),
    @('Chỉ nhận diện và thống kê ĐƯỜNG ỐNG: đọc DN/D/Ø, geometry, topology và Graph/GNN khi có model. Không chạy sang VAN / THIẾT BỊ.',
      'Nhận diện chung PIPE + ỐNG GIÓ trên một vùng quét: DN/D/Ø cho pipe, WxH/ØD + EI cho duct. Không chạy sang VAN / THIẾT BỊ.'),
    @('Chỉ nhận diện VAN / THIẾT BỊ. STEP21: Exact Block → Vector Fingerprint → ONNX Classifier → Vision Lite → Pipe Context. Nếu chưa có model ONNX, các tầng cũ vẫn chạy bình thường.',
      'Nhận diện VAN / THIẾT BỊ + HVAC: VCD/OBD/FD/FSD/MD/NRD, miệng gió/diffuser/grille, FCU/AHU... Exact Block → Vector → ONNX/YOLO → Context; thêm HVAC semantic từ Block/Layer/Attribute + duct size.')
)

$changed = 0

foreach ($pair in $replacements) {
    $old = $pair[0]
    $new = $pair[1]

    if ($text.Contains($old)) {
        $text = $text.Replace($old, $new)
        $changed++
        Write-Host "OK: $old"
    }
    else {
        Write-Host "SKIP (không thấy): $old"
    }
}

Set-Content $xaml $text -Encoding UTF8

Write-Host ""
Write-Host "STEP30B-D3 XAML patch xong. Số mục thay: $changed"
Write-Host "Backup: $backup"
Write-Host "Tiếp theo: Save All -> Clean Solution -> Rebuild Debug|x64"
