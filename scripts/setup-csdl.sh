#!/usr/bin/env bash
# Điền chuỗi kết nối CSDL chung vào appsettings.Development.json của API.
#
# Dùng:  1. Copy chuỗi kết nối trong tin nhắn GHIM ở chat nhóm
#        2. bash scripts/setup-csdl.sh
#
# Script lấy chuỗi từ clipboard (không có clipboard thì cho dán vào terminal), tạo
# backend/SmartBus.Api/appsettings.Development.json từ file .example rồi thay chỗ dán mẫu
# bằng chuỗi đó. Chạy lại khi đã cấu hình thì bỏ qua, không ghi đè — muốn ghi đè: --force.
# Chuỗi kết nối không bao giờ bị in ra màn hình và không nằm trong repo (repo public).
#
# Bản PowerShell cho ai không dùng Git Bash: scripts/setup-csdl.ps1
set -euo pipefail

GOC="$(cd "$(dirname "$0")/.." && pwd)"
DICH="$GOC/backend/SmartBus.Api/appsettings.Development.json"
MAU="$GOC/backend/SmartBus.Api/appsettings.Development.json.example"
CHO_DAN="DAN_CHUOI_KET_NOI_CSDL_CHUNG_VAO_DAY"

FORCE=0
if [ "${1:-}" = "--force" ]; then
    FORCE=1
fi

if [ ! -f "$MAU" ]; then
    echo "Không thấy file mẫu: $MAU" >&2
    echo "Bạn đang chạy script từ ngoài repo? Hãy chạy: bash scripts/setup-csdl.sh" >&2
    exit 1
fi

if [ "$FORCE" -eq 0 ] && [ -f "$DICH" ] && ! grep -q "$CHO_DAN" "$DICH"; then
    echo "Đã cấu hình rồi ($DICH) — không cần chạy lại."
    echo "Muốn ghi đè (chuỗi mới): bash scripts/setup-csdl.sh --force"
    exit 0
fi

CHUOI=""

# 1) Chạy qua đường ống (kiểm thử tự động) — đọc chuỗi từ stdin.
if [ ! -t 0 ]; then
    CHUOI="$(head -n 1 || true)"
fi

# 2) Đọc từ clipboard — Windows (Git Bash gọi được powershell.exe), macOS, Linux.
if [ -z "$CHUOI" ]; then
    if command -v powershell.exe >/dev/null 2>&1; then
        CHUOI="$(powershell.exe -NoProfile -Command Get-Clipboard 2>/dev/null | head -n 1 || true)"
    elif command -v pbpaste >/dev/null 2>&1; then
        CHUOI="$(pbpaste 2>/dev/null | head -n 1 || true)"
    elif command -v xclip >/dev/null 2>&1; then
        CHUOI="$(xclip -selection clipboard -o 2>/dev/null | head -n 1 || true)"
    elif command -v wl-paste >/dev/null 2>&1; then
        CHUOI="$(wl-paste 2>/dev/null | head -n 1 || true)"
    fi
fi

# 3) Vẫn chưa có — bảo người dùng dán vào terminal.
if [ -z "$CHUOI" ]; then
    echo "Copy chuỗi kết nối trong tin nhắn ghim ở chat nhóm, dán vào đây rồi Enter:"
    read -r CHUOI || true
fi

CHUOI="$(printf '%s' "$CHUOI" | tr -d '\r\n' | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"

if [ -z "$CHUOI" ]; then
    echo "Chưa nhận được chuỗi kết nối — không ghi gì cả." >&2
    exit 1
fi

if ! printf '%s' "$CHUOI" | grep -qi 'host='; then
    echo "Chuỗi nhận được không giống chuỗi kết nối CSDL (thiếu 'Host=') — không ghi gì cả." >&2
    echo "Nhớ copy NGUYÊN chuỗi trong tin nhắn ghim, đừng copy nhầm dòng khác." >&2
    exit 1
fi

if printf '%s' "$CHUOI" | grep -q '["\\]'; then
    echo 'Chuỗi chứa ký tự " hoặc \ — không dán được vào JSON, không ghi gì cả.' >&2
    echo "Copy lại nguyên chuỗi trong tin nhắn ghim xem có dính ký tự lạ không." >&2
    exit 1
fi

# Thay bằng sed: escape \, & và dấu phân cách | của chính lệnh sed.
CHUOI_ESCAPE="$(printf '%s' "$CHUOI" | sed -e 's/[\\&|]/\\&/g')"
sed "s|$CHO_DAN|$CHUOI_ESCAPE|" "$MAU" > "$DICH.tam"
mv "$DICH.tam" "$DICH"

if grep -q "$CHO_DAN" "$DICH"; then
    echo "Thay chuỗi thất bại — file $DICH còn nguyên chỗ dán mẫu." >&2
    exit 1
fi

echo "Đã ghi chuỗi kết nối vào: $DICH (không in chuỗi ra màn hình)."
echo "Tiếp theo:  dotnet run --project backend/SmartBus.Api"
echo "Hướng dẫn đầy đủ: docs/25-huong-dan-csdl-chung.md"
