import { theme } from 'antd';

// Đoạn tuyến ●—○ dùng chung: điểm đi là chấm đặc, điểm đến là chấm rỗng — đúng quy ước
// "đã qua / sắp tới" của sơ đồ tuyến. Xuất hiện trên thẻ chuyến, đầu màn chọn ghế và vé.
//
// Bề rộng đoạn nối cố định (không co giãn) để mọi chỗ hiển thị giống nhau, không phụ
// thuộc bề rộng khung chứa.
export default function RouteLine({
  from,
  to,
  size = 'md',
}: {
  from: string;
  to: string;
  size?: 'sm' | 'md';
}) {
  const { token } = theme.useToken();
  const dotSize = size === 'sm' ? 7 : 9;
  const connectorWidth = size === 'sm' ? 28 : 44;
  const fontSize = size === 'sm' ? 12 : 13;
  const textStyle = {
    fontSize,
    color: token.colorTextSecondary,
    whiteSpace: 'nowrap' as const,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  };

  return (
    <span
      style={{ display: 'inline-flex', alignItems: 'center', gap: 8, maxWidth: '100%', minWidth: 0 }}
    >
      <span
        aria-hidden
        style={{
          flex: '0 0 auto',
          width: dotSize,
          height: dotSize,
          borderRadius: '50%',
          background: token.colorPrimary,
        }}
      />
      <span style={textStyle}>{from}</span>
      <span
        aria-hidden
        style={{
          flex: '0 0 auto',
          width: connectorWidth,
          height: 2,
          borderRadius: 1,
          background: token.colorBorderSecondary,
        }}
      />
      <span
        aria-hidden
        style={{
          flex: '0 0 auto',
          width: dotSize,
          height: dotSize,
          borderRadius: '50%',
          border: `2px solid ${token.colorPrimary}`,
          background: token.colorBgContainer,
        }}
      />
      <span style={textStyle}>{to}</span>
    </span>
  );
}
