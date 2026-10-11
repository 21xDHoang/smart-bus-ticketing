import { theme } from 'antd';
import { CheckOutlined } from '@ant-design/icons';

// Tiến trình đặt vé — bốn ga dọc tuyến: Chọn chuyến → Chọn ghế → Thanh toán → Nhận vé.
// Cùng ngôn ngữ với sơ đồ tuyến: ga đã qua là chấm đặc (có dấu tick), ga đang đứng được
// khoanh vòng, ga phía trước là chấm rỗng. Màn hẹp chỉ giữ nhãn của ga đang đứng
// (xem .booking-progress trong index.css).

export type BookingStep = 'trip' | 'seat' | 'payment' | 'ticket';

const STEPS: { key: BookingStep; label: string }[] = [
  { key: 'trip', label: 'Chọn chuyến' },
  { key: 'seat', label: 'Chọn ghế' },
  { key: 'payment', label: 'Thanh toán' },
  { key: 'ticket', label: 'Nhận vé' },
];

export default function BookingProgress({ current }: { current: BookingStep }) {
  const { token } = theme.useToken();
  const currentIndex = STEPS.findIndex((step) => step.key === current);

  return (
    <nav aria-label="Tiến trình đặt vé" style={{ marginBottom: 20 }}>
      <ol
        className="booking-progress"
        style={{ display: 'flex', alignItems: 'center', margin: 0, padding: 0, listStyle: 'none' }}
      >
        {STEPS.map((step, index) => {
          const isDone = index < currentIndex;
          const isCurrent = index === currentIndex;

          return (
            <li
              key={step.key}
              aria-current={isCurrent ? 'step' : undefined}
              style={{
                display: 'flex',
                alignItems: 'center',
                // Ga cuối không cần co giãn — các đoạn nối giữa các ga mới co giãn.
                flex: index === STEPS.length - 1 ? '0 0 auto' : '1 1 0',
              }}
            >
              <span style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    flex: '0 0 auto',
                    width: 16,
                    height: 16,
                    borderRadius: '50%',
                    background: isDone || isCurrent ? token.colorPrimary : token.colorBgContainer,
                    border: `2px solid ${isDone || isCurrent ? token.colorPrimary : token.colorBorder}`,
                    // Ga đang đứng được khoanh vòng nhạt — mắt bắt được ngay đang ở đâu.
                    boxShadow: isCurrent ? `0 0 0 4px ${token.colorPrimaryBg}` : undefined,
                  }}
                >
                  {isDone && (
                    <CheckOutlined style={{ fontSize: 9, color: token.colorTextLightSolid }} />
                  )}
                </span>
                <span
                  className={`bp-label${isCurrent ? ' bp-label-current' : ''}`}
                  style={{
                    fontSize: 13,
                    fontWeight: isCurrent ? 700 : 500,
                    color: isCurrent
                      ? token.colorTextHeading
                      : isDone
                        ? token.colorText
                        : token.colorTextSecondary,
                    whiteSpace: 'nowrap',
                  }}
                >
                  {step.label}
                </span>
              </span>
              {index < STEPS.length - 1 && (
                <span
                  aria-hidden
                  style={{
                    flex: '1 1 auto',
                    height: 2,
                    minWidth: 12,
                    margin: '0 12px',
                    borderRadius: 1,
                    background: isDone ? token.colorPrimary : token.colorBorder,
                  }}
                />
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
