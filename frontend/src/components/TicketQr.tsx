import { useMemo } from 'react';
import { theme } from 'antd';

// Ô mã QR GIẢ — chỉ là hình minh hoạ để chỗ "vé hết hạn / vé đã sử dụng" có thứ để làm mờ.
// Mã QR THẬT là việc của Kiên (service sinh QR, task Sprint 3) và Băng (màn hình hiển thị QR +
// tăng sáng màn hình) — component này không thay thế chúng, chỉ vẽ một lưới đen/trắng nhìn
// giống QR, cùng một mã vé luôn ra cùng một hình. Dùng `disabled` để bôi xám vé không còn
// quét được (đã sử dụng / hết hạn / đã huỷ).

/** Số module của mã QR chuẩn nhỏ nhất — 21×21. */
const SIZE = 21;

/** Băm chuỗi mã vé thành số nguyên 32 bit — cùng mã vé luôn ra cùng một hình. */
function hashString(input: string): number {
  let hash = 2166136261;
  for (let i = 0; i < input.length; i += 1) {
    hash ^= input.charCodeAt(i);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}

/** Một ô có nằm trong vùng "mắt" 7×7 ở một trong ba góc không (kể cả vành trắng quanh mắt). */
function inFinderArea(row: number, col: number): boolean {
  const nearTop = row < 7;
  const nearBottom = row >= SIZE - 7;
  const nearLeft = col < 7;
  const nearRight = col >= SIZE - 7;
  return (nearTop && nearLeft) || (nearTop && nearRight) || (nearBottom && nearLeft);
}

/** Ô đen của "mắt" (vành ngoài + lõi 3×3) — phần trắng giữa hai lớp để trống. */
function isFinderFilled(row: number, col: number): boolean {
  const local = (r0: number, c0: number) => {
    const dr = row - r0;
    const dc = col - c0;
    if (dr < 0 || dr > 6 || dc < 0 || dc > 6) return false;
    const onRing = dr === 0 || dr === 6 || dc === 0 || dc === 6;
    const onCore = dr >= 2 && dr <= 4 && dc >= 2 && dc <= 4;
    return onRing || onCore;
  };

  return local(0, 0) || local(0, SIZE - 7) || local(SIZE - 7, 0);
}

/** Dựng lưới đen/trắng từ mã vé — vùng mắt cố định, phần còn lại sinh ngẫu nhiên theo mã. */
function buildMatrix(code: string): boolean[][] {
  const matrix = Array.from({ length: SIZE }, () => Array<boolean>(SIZE).fill(false));

  let seed = hashString(code);
  const rand = () => {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    return seed / 0x100000000;
  };

  for (let row = 0; row < SIZE; row += 1) {
    for (let col = 0; col < SIZE; col += 1) {
      matrix[row][col] = inFinderArea(row, col) ? isFinderFilled(row, col) : rand() < 0.45;
    }
  }

  return matrix;
}

interface TicketQrProps {
  /** Mã vé — nguồn sinh hình. */
  code: string;
  /** true = vé không còn quét được (đã sử dụng / hết hạn / đã huỷ) → bôi xám. */
  disabled?: boolean;
}

export default function TicketQr({ code, disabled = false }: TicketQrProps) {
  const { token } = theme.useToken();
  const matrix = useMemo(() => buildMatrix(code), [code]);

  return (
    <div
      style={{
        width: 96,
        height: 96,
        padding: 8,
        background: token.colorBgContainer,
        border: `1px solid ${token.colorBorder}`,
        borderRadius: token.borderRadius,
        color: disabled ? token.colorTextDisabled : token.colorTextHeading,
        opacity: disabled ? 0.55 : 1,
      }}
    >
      <svg
        viewBox={`0 0 ${SIZE} ${SIZE}`}
        width="100%"
        height="100%"
        shapeRendering="crispEdges"
        role="img"
        aria-label={`Mã QR ${code}`}
      >
        {matrix.flatMap((row, r) =>
          row.map((filled, c) =>
            filled ? (
              <rect key={`${r}-${c}`} x={c} y={r} width={1} height={1} fill="currentColor" />
            ) : null,
          ),
        )}
      </svg>
    </div>
  );
}
