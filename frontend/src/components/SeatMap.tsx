import { theme } from 'antd';
import type { CSSProperties, ReactNode } from 'react';
import { seatKey, seatNumber } from '../api/seatLayoutApi';

// Sơ đồ ghế trực quan — dùng chung cho màn cấu hình (xem nhanh trong bảng) và modal (bấm
// chọn ghế VIP). Chỉ là khung vẽ, không gọi API: nhận số tầng / hàng / cột và tập ghế VIP từ
// ngoài truyền vào.
//
// Vì sao tách thành component: cùng một cách vẽ được dùng ở hai chỗ (xem nhanh trong bảng +
// chọn VIP trong modal), chép tay là hai bản dễ lệch nhau.

interface SeatMapProps {
  floors: number;
  rowsPerFloor: number;
  columnsPerRow: number;
  /** Khoá các ghế VIP ("1-1-1" = tầng 1, hàng 1, cột 1). Ghế còn lại là ghế thường. */
  vipKeys: string[];
  /** Khi có, bấm ghế để chọn/bỏ VIP (dùng trong modal). */
  onToggleVip?: (key: string) => void;
  /** true khi hiển thị trong modal (có thể bấm); false khi chỉ xem. */
  interactive?: boolean;
}

export default function SeatMap({
  floors,
  rowsPerFloor,
  columnsPerRow,
  vipKeys,
  onToggleVip,
  interactive = false,
}: SeatMapProps) {
  const { token } = theme.useToken();
  const vipSet = new Set(vipKeys);

  // Cắt đôi số cột để chừa lối đi ở giữa — xe 4 ghế/hàng trở lên mới có lối đi rõ rệt.
  const leftCols = Math.ceil(columnsPerRow / 2);
  const hasAisle = columnsPerRow >= 4;

  const renderSeat = (floor: number, row: number, col: number) => {
    const key = seatKey(floor, row, col);
    const number = seatNumber(row - 1, col - 1);
    const isVip = vipSet.has(key);

    const style: CSSProperties = {
      width: 30,
      height: 30,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: token.borderRadius,
      border: `1px solid ${isVip ? token.colorWarning : token.colorBorder}`,
      background: isVip ? token.colorWarning : '#ffffff',
      color: isVip ? '#ffffff' : token.colorTextSecondary,
      fontSize: 11,
      fontWeight: isVip ? 700 : 500,
      cursor: interactive ? 'pointer' : 'default',
      userSelect: 'none',
      padding: 0,
      lineHeight: 1,
    };

    return (
      <button
        type="button"
        key={key}
        style={style}
        title={interactive ? `${number} — ${isVip ? 'ghế VIP' : 'ghế thường'}` : `${number}`}
        aria-pressed={isVip}
        onClick={interactive && onToggleVip ? () => onToggleVip(key) : undefined}
      >
        {number}
      </button>
    );
  };

  const renderRow = (floor: number, row: number) => {
    const cells: ReactNode[] = [];
    for (let col = 1; col <= columnsPerRow; col += 1) {
      // Chèn khoảng lối đi trước nửa bên phải — ghế vẫn đánh số liên tục, lối đi chỉ là hình.
      if (hasAisle && col === leftCols + 1) {
        cells.push(<div key="aisle" style={{ width: 20, flexShrink: 0 }} />);
      }
      cells.push(renderSeat(floor, row, col));
    }

    return (
      <div
        key={row}
        style={{ display: 'flex', justifyContent: 'center', gap: 6, marginBottom: 6 }}
      >
        {cells}
      </div>
    );
  };

  return (
    <div>
      {Array.from({ length: floors }, (_, floorIndex) => {
        const floor = floorIndex + 1;
        return (
          <div key={floor} style={{ marginBottom: floors > 1 ? 16 : 0 }}>
            {floors > 1 && (
              <div
                style={{
                  textAlign: 'center',
                  color: token.colorTextSecondary,
                  fontSize: 12,
                  marginBottom: 8,
                }}
              >
                Tầng {floor}
              </div>
            )}
            {Array.from({ length: rowsPerFloor }, (_, rowIndex) =>
              renderRow(floor, rowIndex + 1),
            )}
          </div>
        );
      })}

      <div
        style={{
          display: 'flex',
          gap: 16,
          marginTop: 12,
          fontSize: 12,
          color: token.colorTextSecondary,
          flexWrap: 'wrap',
        }}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <span
            style={{
              width: 12,
              height: 12,
              borderRadius: 4,
              border: `1px solid ${token.colorBorder}`,
              background: '#ffffff',
            }}
          />
          Ghế thường
        </span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <span
            style={{ width: 12, height: 12, borderRadius: 4, background: token.colorWarning }}
          />
          Ghế VIP
        </span>
        {hasAisle && (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <span
              style={{ width: 12, height: 12, borderRadius: 4, background: token.colorBgLayout }}
            />
            Lối đi
          </span>
        )}
      </div>
    </div>
  );
}
