import { QRCodeSVG } from 'qrcode.react';
import { theme } from 'antd';

// Mã QR THẬT của vé điện tử — thay bản "QR giả" trước đây (chỉ vẽ lưới đen/trắng minh hoạ,
// không quét được). Component này sinh mã QR quét được từ `code` (mã vé do backend cấp — việc
// SINH mã thuộc Nguyễn Duy Kiên, việc trình bày thuộc màn hình của Nguyễn Đình Băng). Dùng
// chung cho hai chỗ: danh sách "Vé của tôi" (nhỏ) và màn hình "Mã QR vé" (phóng to + tăng sáng).
// Vé không còn quét được (đã dùng / hết hạn / đã huỷ) thì `disabled` bôi mờ.

interface TicketQrProps {
  /** Mã vé = nội dung mã QR soát vé. Duy nhất toàn hệ thống. */
  code: string;
  /** Cạnh (px) của mã QR — chưa tính lề trắng và viền. Mặc định 96 cho danh sách. */
  size?: number;
  /** true = vé không còn quét được → bôi mờ để khách biết không dùng được nữa. */
  disabled?: boolean;
}

export default function TicketQr({ code, size = 96, disabled = false }: TicketQrProps) {
  const { token } = theme.useToken();

  return (
    <div
      style={{
        display: 'inline-flex',
        padding: 10,
        background: '#ffffff',
        border: `1px solid ${token.colorBorder}`,
        borderRadius: token.borderRadius,
        opacity: disabled ? 0.35 : 1,
        lineHeight: 0,
      }}
    >
      <QRCodeSVG
        value={code}
        size={size}
        level="M"
        marginSize={4}
        bgColor="#ffffff"
        fgColor="#000000"
        title={`Mã QR vé ${code}`}
      />
    </div>
  );
}
