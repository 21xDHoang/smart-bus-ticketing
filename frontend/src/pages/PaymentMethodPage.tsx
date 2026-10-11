import { useEffect, useMemo, useState } from 'react';
import type { CSSProperties } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import { Alert, Button, Col, Row, Space, Tag, Typography, message, theme } from 'antd';
import dayjs from 'dayjs';
import paymentApi from '../api/paymentApi';
import type { PaymentMethodCode, PaymentMethodOption } from '../api/paymentApi';
import type { AppError } from '../api/axiosClient';
import seatHoldApi from '../api/seatHoldApi';
import type { SeatHoldSession } from '../api/seatHoldApi';
import seatHoldReleaseApi from '../api/seatHoldReleaseApi';
import type { VoucherValidationResponse } from '../api/voucherApi';
import { formatTime } from '../components/ui/format';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import SeatHoldCountdown from '../components/SeatHoldCountdown';
import SeatHoldConnectionNotice from '../components/SeatHoldConnectionNotice';
import SeatHoldExpiryModal from '../components/SeatHoldExpiryModal';
import { getSeatHoldTimeInfo } from '../components/seatHoldReminder';
import { useSeatHoldAutoRelease } from '../components/useSeatHoldAutoRelease';
import VoucherCodeInput from '../components/VoucherCodeInput';
import VoucherDiscountSummary from '../components/VoucherDiscountSummary';

const { Text } = Typography;

/** Kết quả một lượt tải danh sách phương thức thanh toán — ba trạng thái tách bạch. */
type Outcome =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; methods: PaymentMethodOption[] };

// Màn hình chọn phương thức thanh toán (User Story 6 "Cổng thanh toán", Sprint 4 — task của
// Dương Thị Hạnh). Là bước tiếp theo của luồng tra cứu tuyến → kết quả tìm kiếm chuyến →
// sơ đồ ghế: hành khách xem lại tóm tắt đặt vé và chọn kênh thanh toán. Bảng Payments đã
// migrate nhưng endpoint thanh toán chưa có (chờ controller của Hiếu/Kiên) nên bước trả tiền
// còn dùng dữ liệu giả (paymentApi.ts).
//
// Nhận ngữ cảnh qua query string, cùng lối SeatMapPage: `tripId` (bắt buộc) + `routeName`,
// `departureTime`, `busType`, `seats` (số ghế cách nhau bằng dấu phẩy) và `total` (tổng tiền
// VND) để hiển thị. Các trường ngoài `tripId` đều là ngữ cảnh do màn trước truyền sang —
// thiếu thì vẫn mở được màn hình, chỉ là phần tóm tắt hiện chỗ trống tương ứng.
//
// Phiên giữ chỗ (US 3): màn chọn ghế ĐÃ giữ ghế trước khi chuyển sang đây — nhận `sessionCode`
// qua query kèm cả phiên qua state điều hướng để đếm ngược có ngay không phải chờ mạng (F5 thì
// tra lại bằng GET /seat-holds/{sessionCode}). Suốt bước thanh toán, cụm giữ chỗ đứng trên đầu
// màn hình: banner đếm ngược + banner mất kết nối, modal cảnh báo sắp hết hạn kèm nút gia hạn,
// và tự động nhả ghế + quay về chọn ghế khi đồng hồ về 0. Khách chủ động "Quay về chọn ghế"
// thì nhả phiên luôn, không đợi job nền.
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

  // Kết quả kiểm tra mã giảm giá — do VoucherCodeInput (r56) đẩy lên qua onValidationChange,
  // dùng chung cho VoucherDiscountSummary (r57) để hiển thị số tiền được giảm và giá sau giảm.
  // Kết quả này chỉ là XEM TRƯỚC, không phải số tiền chốt khi thanh toán.
  const [validation, setValidation] = useState<VoucherValidationResponse | null>(null);

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

  // ── Phiên giữ chỗ (US 3) ──────────────────────────────────────────────────────────────────
  // Màn chọn ghế đã tạo phiên (POST /seat-holds) rồi chuyển sang đây kèm sessionCode. Ưu tiên
  // phiên đi kèm state điều hướng để đếm ngược có ngay khung hình đầu; mở thẳng / tải lại
  // trang (F5) thì không còn state — tra lại phiên theo mã.
  const location = useLocation();
  const sessionCode = searchParams.get('sessionCode')?.trim() ?? '';
  const routedSession =
    (location.state as { seatHold?: SeatHoldSession } | null)?.seatHold ?? null;

  const [session, setSession] = useState<SeatHoldSession | null>(routedSession);
  const [holdLookupFailed, setHoldLookupFailed] = useState(false);
  const [expiryDismissed, setExpiryDismissed] = useState(false);
  const [holdNow, setHoldNow] = useState(() => dayjs());

  useEffect(() => {
    if (!sessionCode || session) return;

    let cancelled = false;

    seatHoldApi
      .getSession(sessionCode)
      .then((fetched) => {
        if (!cancelled) setSession(fetched);
      })
      .catch(() => {
        if (!cancelled) setHoldLookupFailed(true);
      });

    return () => {
      cancelled = true;
    };
  }, [sessionCode, session]);

  // Đồng hồ 1 giây dùng CHUNG cho banner đếm ngược và modal (truyền xuống qua prop `now`) —
  // mỗi mảnh tự tick sẽ lệch nhau một nhịp. Chỉ chạy khi phiên còn đang giữ.
  useEffect(() => {
    if (session?.status !== 'Holding') return;

    const timer = window.setInterval(() => setHoldNow(dayjs()), 1000);
    return () => window.clearInterval(timer);
  }, [session]);

  const holdInfo = session ? getSeatHoldTimeInfo(session, holdNow) : null;

  // Đường về màn chọn ghế, giữ đúng ngữ cảnh chuyến (SeatMapPage đọc `busType` cho phần đầu
  // trang) — dùng chung cho lượt tự nhả khi hết hạn và nút "Quay về chọn ghế".
  const seatMapParams = new URLSearchParams({ tripId });
  if (routeName) seatMapParams.set('routeName', routeName);
  if (departureTime) seatMapParams.set('departureTime', departureTime);
  if (busType) seatMapParams.set('busType', busType);
  const seatMapUrl = `/seat-map?${seatMapParams.toString()}`;

  // Hết hạn (đồng hồ về 0, hoặc phiên đã khép lại từ trước): hook tự nhả ghế best-effort rồi
  // gọi callback đúng MỘT lần — đưa khách về chọn ghế kèm lời báo thay vì để đứng lại.
  useSeatHoldAutoRelease(session, () => {
    message.warning('Phiên giữ chỗ đã hết hạn — ghế đã được nhả. Hãy chọn lại ghế.');
    navigate(seatMapUrl, { replace: true });
  });

  // "Quay về chọn ghế" khi khách chủ động bỏ phiên — đúng mục đích "khách huỷ thao tác" của
  // endpoint release: nhả ngay cho người sau, không đợi job nền. Best-effort: lỗi không chặn
  // việc rời trang (job nền vẫn tự nhả khi hết hạn).
  const handleBackToSeats = () => {
    if (session) {
      seatHoldReleaseApi.release(session.sessionCode).catch(() => {
        // Bỏ qua: nhả best-effort, không đổi kết cục của người dùng.
      });
    }
    navigate(seatMapUrl, { replace: true });
  };

  // Modal cảnh báo bật khi phiên sắp hết hạn hoặc đã hết hạn; `expiryDismissed` xuống lại
  // false sau mỗi lượt gia hạn để cửa sổ mới còn nhắc được khi lại gần hết giờ.
  const expiryOpen =
    session !== null && holdInfo !== null && !expiryDismissed && holdInfo.phase !== 'Holding';

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

      {/* Cụm giữ chỗ (US 3) — đứng trên đầu suốt bước thanh toán. Banner đếm ngược và banner
          mất kết nối tự ẩn khi phiên khép lại (Confirmed/Released). */}
      {session && (
        <Space direction="vertical" size={8} style={{ display: 'flex', marginBottom: 16 }}>
          <SeatHoldCountdown session={session} now={holdNow} />
          <SeatHoldConnectionNotice session={session} onResync={setSession} />
        </Space>
      )}

      {holdLookupFailed && (
        <Alert
          type="warning"
          showIcon
          message="Chưa kiểm tra được phiên giữ chỗ"
          description="Không tra được phiên giữ chỗ theo mã. Nếu đã quá giờ giữ, ghế có thể đã được nhả — hãy kiểm tra lại trước khi thanh toán."
          style={{ marginBottom: 16 }}
          action={
            <Button size="small" onClick={handleBackToSeats}>
              Quay về chọn ghế
            </Button>
          }
        />
      )}

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

              <div style={{ margin: '12px 0' }}>
                <Text type="secondary">Mã giảm giá</Text>
                <div style={{ marginTop: 4 }}>
                  <VoucherCodeInput
                    tripId={tripId}
                    orderAmount={hasTotal ? totalPrice : null}
                    validation={validation}
                    onValidationChange={setValidation}
                    disabled={paying}
                  />
                </div>
              </div>

              <div style={{ marginBottom: 16 }}>
                <VoucherDiscountSummary
                  orderAmount={hasTotal ? totalPrice : null}
                  validation={validation}
                />
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

      {/* Modal sắp / đã hết hạn kèm nút gia hạn (r21) — chỉ mở khi thật sự cần; khách đóng thì
          `expiryDismissed` giữ yên cho tới lượt gia hạn kế tiếp hoặc khi quay lại chọn ghế. */}
      <SeatHoldExpiryModal
        open={expiryOpen}
        session={session}
        now={holdNow}
        onClose={() => setExpiryDismissed(true)}
        onBackToSeats={handleBackToSeats}
        onExtended={(renewed) => {
          setSession(renewed);
          setExpiryDismissed(false);
        }}
      />
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
  method: PaymentMethodOption;
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
