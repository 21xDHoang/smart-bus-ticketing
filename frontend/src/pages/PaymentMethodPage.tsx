import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Col, Row, Space, Tag, Typography, message, theme } from 'antd';
import paymentApi from '../api/paymentApi';
import type { PaymentMethod, PaymentMethodCode } from '../api/paymentApi';
import type { AppError } from '../api/axiosClient';
import { formatTime, formatVnd } from '../components/ui/format';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';

const { Text } = Typography;

/** Kết quả một lượt tải danh sách phương thức thanh toán — ba trạng thái tách bạch. */
type Outcome =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; methods: PaymentMethod[] };

// Màn hình chọn phương thức thanh toán (User Story 6 "Cổng thanh toán", Sprint 4 — task của
// Dương Thị Hạnh). Là bước tiếp theo của luồng tra cứu tuyến → kết quả tìm kiếm chuyến →
// sơ đồ ghế: hành khách xem lại tóm tắt đặt vé và chọn kênh thanh toán. Backend chưa có
// bảng Payments và chưa có endpoint thanh toán nên màn hình chạy dữ liệu giả (paymentApi.ts).
//
// Nhận ngữ cảnh qua query string, cùng lối SeatMapPage: `tripId` (bắt buộc) + `routeName`,
// `departureTime`, `busType`, `seats` (số ghế cách nhau bằng dấu phẩy) và `total` (tổng tiền
// VND) để hiển thị. Các trường ngoài `tripId` đều là ngữ cảnh do màn trước truyền sang —
// thiếu thì vẫn mở được màn hình, chỉ là phần tóm tắt hiện chỗ trống tương ứng.
export default function PaymentMethodPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { token } = theme.useToken();

  const tripId = searchParams.get('tripId')?.trim() ?? '';
  const routeName = searchParams.get('routeName')?.trim() ?? '';
  const departureTime = searchParams.get('departureTime')?.trim() ?? '';
  const busType = searchParams.get('busType')?.trim() ?? '';

  // Các số ghế đã chọn, tách từ `seats=A1,A2` — chỉ để hiển thị, không phải khoá nghiệp vụ.
  const seatNumbers = useMemo(() => {
    const raw = searchParams.get('seats')?.trim() ?? '';
    return raw
      .split(',')
      .map((seat) => seat.trim())
      .filter(Boolean);
  }, [searchParams]);

  // Tổng tiền tạm tính — do màn sơ đồ ghế tính rồi truyền sang để hiển thị, không tính lại.
  const totalParam = searchParams.get('total')?.trim() ?? '';
  const totalPrice = Number(totalParam);
  const hasTotal = totalParam !== '' && Number.isFinite(totalPrice);

  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [selectedCode, setSelectedCode] = useState<PaymentMethodCode | null>(null);
  const [paying, setPaying] = useState(false);

  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API — đây là lần tải thực sự, không phải đồng bộ
    // state dẫn xuất nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setOutcome({ status: 'loading' });

    paymentApi
      .listMethods()
      .then((methods) => {
        if (cancelled) return;
        setOutcome({ status: 'done', methods });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setOutcome({
          status: 'error',
          message: appError.customMessage || 'Không thể tải danh sách phương thức thanh toán.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadToken]);

  const handlePay = async () => {
    if (!selectedCode) return;

    setPaying(true);
    try {
      await paymentApi.pay({
        methodCode: selectedCode,
        tripId,
        seatNumbers,
        total: hasTotal ? totalPrice : 0,
      });
      message.success('Thanh toán thành công. Vé của bạn đã được ghi nhận.');
      navigate('/my-tickets');
    } catch (err) {
      const appError = err as AppError;
      message.error(appError.customMessage || 'Không thể hoàn tất thanh toán. Vui lòng thử lại.');
    } finally {
      setPaying(false);
    }
  };

  // Chưa có tripId → không có chuyến nào để thanh toán. Mời quay lại luồng tra cứu.
  if (!tripId) {
    return (
      <EmptyState
        description="Chưa chọn chuyến xe. Hãy quay lại kết quả tìm kiếm để chọn chuyến rồi chọn ghế."
        action={
          <Button type="primary" onClick={() => navigate('/route-lookup')}>
            Quay lại tra cứu tuyến
          </Button>
        }
      />
    );
  }

  // Gọi API hỏng → trạng thái riêng, không mạo nhận là "không có phương thức nào".
  if (outcome?.status === 'error') {
    return (
      <ErrorState
        title="Không tải được phương thức thanh toán"
        description={outcome.message}
        onRetry={() => setReloadToken((n) => n + 1)}
      />
    );
  }

  if (outcome?.status !== 'done') {
    return <LoadingState description="Đang tải phương thức thanh toán…" />;
  }

  return (
    <div>
      <PageHeader
        title="Chọn phương thức thanh toán"
        subtitle={[routeName, departureTime ? formatTime(departureTime) : '', busType]
          .filter(Boolean)
          .join(' · ')}
        onBack={() => navigate(-1)}
      />

      <Row gutter={[16, 16]}>
        {/* Danh sách phương thức thanh toán — mỗi phương thức là một thẻ chọn được. */}
        <Col xs={24} lg={17}>
          <PageCard title="Phương thức thanh toán">
            <Space direction="vertical" size={12} style={{ display: 'flex' }}>
              {outcome.methods.map((method) => (
                <MethodCard
                  key={method.code}
                  method={method}
                  token={token}
                  selected={selectedCode === method.code}
                  onSelect={() => setSelectedCode(method.code)}
                />
              ))}
            </Space>
          </PageCard>
        </Col>

        {/* Tóm tắt đặt vé + nút thanh toán — dán cố định khi cuộn, cùng lối SeatMapPage. */}
        <Col xs={24} lg={7}>
          <div style={{ position: 'sticky', top: 88 }}>
            <PageCard title="Tóm tắt đặt vé">
              <SummaryRow label="Tuyến" value={routeName || '—'} />
              <SummaryRow label="Giờ khởi hành" value={departureTime ? formatTime(departureTime) : '—'} />
              <SummaryRow label="Loại xe" value={busType || '—'} />

              <div style={{ margin: '12px 0' }}>
                <Text type="secondary">Ghế đã chọn</Text>
                <div style={{ marginTop: 4 }}>
                  {seatNumbers.length === 0 ? (
                    <Text type="secondary">—</Text>
                  ) : (
                    <Space size={[8, 8]} wrap>
                      {seatNumbers.map((seat) => (
                        <Tag key={seat} color="blue">
                          {seat}
                        </Tag>
                      ))}
                    </Space>
                  )}
                </div>
              </div>

              <div style={{ marginBottom: 16 }}>
                <Text type="secondary">Tổng tiền tạm tính</Text>
                <div style={{ fontSize: 22, fontWeight: 700, color: token.colorPrimary }}>
                  {hasTotal ? formatVnd(totalPrice) : '—'}
                </div>
              </div>

              <Button
                type="primary"
                block
                loading={paying}
                disabled={!selectedCode}
                onClick={handlePay}
              >
                Thanh toán
              </Button>
            </PageCard>
          </div>
        </Col>
      </Row>
    </div>
  );
}

/** Một dòng nhãn → giá trị trong thẻ tóm tắt. */
function SummaryRow({ label, value }: { label: string; value: string }) {
  return (
    <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, marginBottom: 8 }}>
      <Text type="secondary">{label}</Text>
      <Text strong style={{ textAlign: 'right' }}>
        {value}
      </Text>
    </div>
  );
}

/** Một thẻ phương thức thanh toán — bấm để chọn; viền đổi màu khi được chọn. */
function MethodCard({
  method,
  token,
  selected,
  onSelect,
}: {
  method: PaymentMethod;
  token: ReturnType<typeof theme.useToken>['token'];
  selected: boolean;
  onSelect: () => void;
}) {
  const cardStyle: CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    gap: 14,
    width: '100%',
    textAlign: 'left',
    padding: 16,
    borderRadius: token.borderRadiusLG,
    border: `1px solid ${selected ? token.colorPrimary : token.colorBorder}`,
    background: selected ? token.colorPrimaryBg : token.colorBgContainer,
    cursor: 'pointer',
    transition: 'border-color 0.15s, background-color 0.15s',
  };

  return (
    <button type="button" onClick={onSelect} style={cardStyle} aria-pressed={selected}>
      <span
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          width: 44,
          height: 44,
          fontSize: 22,
          borderRadius: '50%',
          background: token.colorFillQuaternary,
          flexShrink: 0,
        }}
      >
        {method.icon}
      </span>

      <span style={{ flex: 1, minWidth: 0 }}>
        <span style={{ display: 'block', fontWeight: 600, color: token.colorText }}>
          {method.name}
        </span>
        <span style={{ display: 'block', color: token.colorTextSecondary, fontSize: 13 }}>
          {method.description}
        </span>
      </span>

      {/* Nút tròn báo chọn — bắt chước radio, đổi màu theo trạng thái. */}
      <span
        style={{
          width: 18,
          height: 18,
          borderRadius: '50%',
          border: `2px solid ${selected ? token.colorPrimary : token.colorBorder}`,
          background: selected ? token.colorPrimary : 'transparent',
          flexShrink: 0,
          transition: 'border-color 0.15s, background-color 0.15s',
        }}
      />
    </button>
  );
}
