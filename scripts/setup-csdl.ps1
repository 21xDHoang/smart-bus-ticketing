# Điền chuỗi kết nối CSDL chung vào appsettings.Development.json của API (bản PowerShell).
#
# Dùng:  1. Copy chuỗi kết nối trong tin nhắn GHIM ở chat nhóm
#        2. powershell -ExecutionPolicy Bypass -File scripts\setup-csdl.ps1
#
# Script lấy chuỗi từ clipboard (không có clipboard thì cho dán vào terminal), tạo
# backend\SmartBus.Api\appsettings.Development.json từ file .example rồi thay chỗ dán mẫu
# bằng chuỗi đó. Chạy lại khi đã cấu hình thì bỏ qua, không ghi đè — muốn ghi đè: -Force.
# Chuỗi kết nối không bao giờ bị in ra màn hình và không nằm trong repo (repo public).
#
# Bản bash cho ai dùng Git Bash: scripts/setup-csdl.sh

[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'

# Xuất tiếng Việt đúng khi chạy trong Windows Terminal / VS Code (PS 5.1 mặc định dùng bảng mã cũ).
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$goc = Split-Path -Parent $PSScriptRoot
$dich = Join-Path $goc 'backend\SmartBus.Api\appsettings.Development.json'
$mau = Join-Path $goc 'backend\SmartBus.Api\appsettings.Development.json.example'
$choDan = 'DAN_CHUOI_KET_NOI_CSDL_CHUNG_VAO_DAY'

if (-not (Test-Path $mau)) {
    Write-Host "Không thấy file mẫu: $mau — bạn đang chạy script từ ngoài repo?" -ForegroundColor Red
    exit 1
}

if (-not $Force -and (Test-Path $dich)) {
    if ((Get-Content -Raw -Encoding UTF8 $dich) -notmatch [regex]::Escape($choDan)) {
        Write-Host "Đã cấu hình rồi ($dich) — không cần chạy lại."
        Write-Host 'Muốn ghi đè (chuỗi mới): thêm -Force'
        exit 0
    }
}

$chuoi = ''

# 1) Chạy qua đường ống (kiểm thử tự động) — đọc chuỗi từ stdin.
if ([Console]::IsInputRedirected) {
    $chuoi = [Console]::In.ReadToEnd()
}

# 2) Đọc từ clipboard.
if ([string]::IsNullOrWhiteSpace($chuoi)) {
    try { $chuoi = Get-Clipboard -Raw } catch { $chuoi = '' }
}

# 3) Vẫn chưa có — bảo người dùng dán vào terminal.
if ([string]::IsNullOrWhiteSpace($chuoi)) {
    Write-Host 'Copy chuỗi kết nối trong tin nhắn ghim ở chat nhóm, dán vào đây rồi Enter:'
    $chuoi = Read-Host
}

# Clipboard nhiều dòng thì lấy dòng không trống đầu tiên.
$dong = @($chuoi -split "`r?`n" | Where-Object { $_.Trim() -ne '' })
$chuoi = if ($dong.Count -gt 0) { $dong[0].Trim() } else { '' }

if ([string]::IsNullOrEmpty($chuoi)) {
    Write-Host 'Chưa nhận được chuỗi kết nối — không ghi gì cả.' -ForegroundColor Red
    exit 1
}

if ($chuoi -notmatch '(?i)host=') {
    Write-Host "Chuỗi nhận được không giống chuỗi kết nối CSDL (thiếu 'Host=') — không ghi gì cả." -ForegroundColor Red
    Write-Host 'Nhớ copy NGUYÊN chuỗi trong tin nhắn ghim, đừng copy nhầm dòng khác.' -ForegroundColor Red
    exit 1
}

if ($chuoi -match '"' -or $chuoi -match '\\') {
    Write-Host 'Chuỗi chứa ký tự " hoặc \ — không dán được vào JSON, không ghi gì cả.' -ForegroundColor Red
    exit 1
}

$noiDung = (Get-Content -Raw -Encoding UTF8 $mau).Replace($choDan, $chuoi)
if ($noiDung -match [regex]::Escape($choDan)) {
    Write-Host 'Thay chuỗi thất bại — không ghi gì cả.' -ForegroundColor Red
    exit 1
}

[System.IO.File]::WriteAllText($dich, $noiDung, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "Đã ghi chuỗi kết nối vào: $dich (không in chuỗi ra màn hình)."
Write-Host 'Tiếp theo:  dotnet run --project backend/SmartBus.Api'
Write-Host 'Hướng dẫn đầy đủ: docs/25-huong-dan-csdl-chung.md'
