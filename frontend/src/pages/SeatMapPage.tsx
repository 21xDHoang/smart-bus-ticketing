import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Col, Row, Space, Typography, message, theme } from 'antd';
import { CloseOutlined } from '@ant-design/icons';
import seatHoldApi from '../api/seatHoldApi';
import seatMapApi from '../api/seatMapApi';
import type { SeatMapSeat, TripSeatMap } from '../api/seatMapApi';
import type { AppError } from '../api/axiosClient';
import { formatVnd, formatTime } from '../components/ui/format';
import {
  BookingProgress,
  EmptyState,
  ErrorState,
  LoadingState,
  PageCard,
  PageHeader,
  RouteBadge,
} from '../components/ui';
import { isSeatSelectable, seatAppearance, seatDisplayName } from '../components/seatStatus';
import { useColorMode } from '../components/ui/colorMode';
import SeatStatusCell from '../components/SeatStatusCell';
import SeatStatusLegend from '../components/SeatStatusLegend';
import { useSeatMapRealtime } from '../realtime/useSeatMapRealtime';

const { Text } = Typography;

/** Kết quả của một lượt gọi API sơ đồ ghế — ba trạng thái tách bạch như mọi màn hình khác. */
type Outcome =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; seatMap: TripSeatMap };

// Màn hình sơ đồ ghế (User Story 2, Sprint 3 — task của Nguyễn Đình Băng): vẽ dàn ghế của một
// chuyến dưới dạng lưới trực quan (có lối đi, tầng xe, ghế VIP) và cho hành khách chọn NHIỀU
// ghế trống cùng lúc. Trạng thái ghế (trống / đang giữ / đã bán) lấy từ endpoint sơ đồ ghế
// (GET /trips/{id}/seats — đã nối API thật, xem seatMapApi.ts).
//
// Nhận `tripId` qua query string; `routeName`, `departureTime`, `busType` là ngữ cảnh do màn
// "Kết quả tìm kiếm chuyến" truyền sang để hiển thị đầu trang, không gọi thêm endpoint nào.
//
// Trạng thái ghế còn được ĐỒNG BỘ REALTIME giữa các phiên (SignalR — task dòng 11 Sprint 3,
// Hoàng Văn Thịnh, hook ở ../realtime/useSeatMapRealtime.ts): nhận sự kiện "sơ đồ ghế vừa đổi"
// thì gọi lại endpoint sơ đồ ghế trong nền — không bật loading, và nếu ghế khách đang chọn vừa
// bị phiên khác giữ / mua thì bỏ khỏi lựa chọn kèm một lời báo.
//
// Bấm "Tiếp tục đặt vé": gọi POST /seat-holds giữ CẢ NHÓM ghế vừa chọn trong 10 phút (US 3,
// SeatHoldApi.create) rồi mới chuyển sang màn chọn phương thức thanh toán kèm `sessionCode` —
// từ đây phiên giữ chỗ theo khách suốt bước thanh toán (đếm ngược, gia hạn, tự nhả khi hết hạn
// ở PaymentMethodPage). Ghế vừa bị phiên khác giữ thì backend trả 409 kèm số ghế: hiện nguyên
// văn máy chủ rồi đồng bộ lại sơ đồ (ghế vừa mất tự rời khỏi lựa chọn).
//
// 11/10/2026 — làm lại giao diện theo hệ thống "Sơ đồ mạng lưới": mã tuyến thành ô màu trên
// đầu trang, dải tiến trình đặt vé, ô ghế phân biệt bằng hình dạng (ô đặc / sọc chéo / viền
// đứt), ghế VIP đánh dấu bằng viền đôi. Toàn bộ phần giữ chỗ, đếm ngược và đồng bộ realtime
// giữ nguyên, chỉ đổi phần vẽ.
export default function SeatMapPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { token } = theme.useToken();

  const tripId = searchParams.get('tripId')?.trim() ?? '';
  const routeName = searchParams.get('routeName')?.trim() ?? '';
  const routeCode = searchParams.get('routeCode')?.trim() ?? '';
  const departureTime = searchParams.get('departureTime')?.trim() ?? '';
  const fallbackBusType = searchParams.get('busType')?.trim() ?? '';

  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [selectedSeatIds, setSelectedSeatIds] = useState<ReadonlySet<string>>(new Set());
  const [hoveredSeatId, setHoveredSeatId] = useState<string | null>(null);

  // Đang gọi POST /seat-holds — chặn bấm hai lần và hiện loading trên nút "Tiếp tục đặt vé".
  const [creatingHold, setCreatingHold] = useState(false);

  useEffect(() => {
    if (!tripId) return;

    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API — đây là lần tải thực sự, không phải đồng bộ
    // state dẫn xuất nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setOutcome({ status: 'loading' });

    seatMapApi
      .getSeatMap(tripId)
      .then((seatMap) => {
        if (cancelled) return;
        setOutcome({ status: 'done', seatMap });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setOutcome({
          status: 'error',
          message: appError.customMessage || 'Không thể tải sơ đồ ghế.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [tripId, reloadToken]);

  const seatMap = outcome?.status === 'done' ? outcome.seatMap : null;

  // ── Đồng bộ realtime giữa các phiên (SignalR) ─────────────────────────────────────────────────
  // Callback nhận sự kiện đọc giá trị MỚI NHẤT qua ref: sự kiện từ websocket rơi vào bất kỳ thời
  // điểm nào, không chờ render, nên closure có thể còn cầm state của lượt render trước đó. Ba ref
  // dưới nhận giá trị hiện tại qua effect đồng bộ mỗi khi state tương ứng đổi.
  const seatMapRef = useRef<TripSeatMap | null>(null);
  const selectedSeatIdsRef = useRef<ReadonlySet<string>>(new Set());
  const tripIdRef = useRef(tripId);

  // Hàng đợi một khe: một lượt gọi API đang bay thì sự kiện mới chỉ đánh dấu "còn nữa"; lượt đang
  // bay xong sẽ chạy tiếp đúng một lượt nữa — nhiều sự kiện dồn dập không mở nhiều request song
  // song, mà cũng không bỏ sót sự kiện cuối cùng.
  const realtimeBusyRef = useRef(false);
  const realtimePendingRef = useRef(false);

  // Trỏ tới bản syncFromServer mới nhất để lượt chạy tiếp trong `finally` không phụ thuộc danh
  // tính hàm.
  const syncRef = useRef<() => void>(() => undefined);

  useEffect(() => {
    seatMapRef.current = seatMap;
    selectedSeatIdsRef.current = selectedSeatIds;
    tripIdRef.current = tripId;
  }, [seatMap, selectedSeatIds, tripId]);

  // Một lượt đồng bộ "im lặng": gọi lại sơ đồ ghế nhưng KHÔNG bật trạng thái loading (không nháy
  // màn hình trong lúc khách đang xem ghế) và hỏng thì giữ nguyên dữ liệu đang hiển thị. Khác hẳn
  // lượt tải đầu ở effect phía trên — lượt đó dựng màn hình lần đầu nên có quyền hiện loading /
  // màn lỗi.
  const syncFromServer = useCallback(() => {
    const currentTripId = tripId;

    if (!currentTripId) return;

    if (realtimeBusyRef.current) {
      realtimePendingRef.current = true;
      return;
    }

    realtimeBusyRef.current = true;

    seatMapApi
      .getSeatMap(currentTripId)
      .then((fresh) => {
        // Chuyến đã đổi trong lúc request đang bay (điều hướng sang chuyến khác trên cùng màn
        // hình) thì bỏ kết quả — sơ đồ của chuyến cũ không được ghi đè chuyến mới.
        if (tripIdRef.current !== currentTripId) return;

        const previous = seatMapRef.current;

        // Ghế "vừa mất" = trước còn trống mà giờ không còn trống nữa (đang giữ / đã bán), hoặc
        // biến mất khỏi sơ đồ. So theo id; ghế mới thêm không ai đang chọn nên không xét.
        const freshById = new Map(fresh.seats.map((seat) => [seat.id, seat]));
        const justLostSeats = previous
          ? previous.seats.filter((seat) => {
              if (!isSeatSelectable(seat.status)) return false;
              const after = freshById.get(seat.id);
              return !after || !isSeatSelectable(after.status);
            })
          : [];

        const lostSelectedSeats = justLostSeats.filter((seat) =>
          selectedSeatIdsRef.current.has(seat.id),
        );

        if (lostSelectedSeats.length > 0) {
          const lostIds = new Set(lostSelectedSeats.map((seat) => seat.id));

          setSelectedSeatIds((prev) => {
            const next = new Set(prev);
            lostIds.forEach((id) => next.delete(id));
            return next;
          });

          // 5 giây — lâu hơn mặc định vì khách đang nhìn sơ đồ, thông báo này giải thích vì sao
          // ghế vừa biến khỏi danh sách đã chọn.
          message.warning(
            `Ghế ${lostSelectedSeats.map((seat) => seatDisplayName(seat)).join(', ')} không còn trống — đã bỏ khỏi lựa chọn của bạn.`,
            5,
          );
        }

        setOutcome({ status: 'done', seatMap: fresh });
      })
      .catch(() => {
        // Nuốt lỗi: giữ nguyên sơ đồ ghế đang hiển thị. Lời nhắc realtime không được phép biến
        // một màn hình đang xem tốt thành màn lỗi — lượt gọi lại kế tiếp (từ sự kiện sau, hoặc
        // nút thao tác) sẽ tự trả lời kênh còn sống hay không.
      })
      .finally(() => {
        realtimeBusyRef.current = false;

        if (realtimePendingRef.current) {
          realtimePendingRef.current = false;
          syncRef.current();
        }
      });
  }, [tripId]);

  useEffect(() => {
    syncRef.current = syncFromServer;
  }, [syncFromServer]);

  useSeatMapRealtime(tripId, syncFromServer);

  // Các ghế đang được chọn, sắp theo tầng → hàng → cột để danh sách dễ đọc.
  const selectedSeats = useMemo(() => {
    if (!seatMap) return [];

    return seatMap.seats
      .filter((seat) => selectedSeatIds.has(seat.id))
      .sort(
        (a, b) => a.floor - b.floor || a.rowIndex - b.rowIndex || a.columnIndex - b.columnIndex,
      );
  }, [seatMap, selectedSeatIds]);

  // Tổng tiền tạm tính. Ghế nào chưa có giá (tuyến chưa cấu hình giá vé) thì không thể cộng —
  // trả null để hiển thị "Chưa có giá" thay vì âm thầm coi ghế đó bằng 0.
  const totalPrice = useMemo(() => {
    if (selectedSeats.some((seat) => seat.price == null)) return null;
    return selectedSeats.reduce((sum, seat) => sum + (seat.price ?? 0), 0);
  }, [selectedSeats]);

  const busType = seatMap?.busType ?? fallbackBusType;

  // Dòng phụ đầu trang dạng chuỗi, đúng như trước; ô mã tuyến (nếu màn trước có truyền) được
  // chèn trước chuỗi này trong JSX.
  const subtitleMeta = [routeName, departureTime ? formatTime(departureTime) : '', busType]
    .filter(Boolean)
    .join(' · ');

  // Bật / tắt chọn một ghế. Chỉ ghế còn trống mới chọn được; ghế đang giữ / đã bán bỏ qua.
  const toggleSeat = (seat: SeatMapSeat) => {
    if (!isSeatSelectable(seat.status)) return;

    setSelectedSeatIds((prev) => {
      const next = new Set(prev);
      if (next.has(seat.id)) {
        next.delete(seat.id);
      } else {
        next.add(seat.id);
      }
      return next;
    });
  };

  // Bấm "Tiếp tục đặt vé": GIỮ GHẾ trước rồi mới sang bước thanh toán (US 3 — giữ chỗ 10 phút
  // trong lúc khách thao tác thanh toán). Ghế gửi lên theo `seat.id`; `seatNumber` chỉ để hiển
  // thị. Ghế vừa bị phiên khác giữ thì backend trả 409 kèm số ghế vướng: hiện nguyên văn máy
  // chủ rồi đồng bộ lại sơ đồ — syncFromServer tự bỏ ghế đã mất khỏi lựa chọn kèm lời báo.
  const handleContinue = async () => {
    if (selectedSeats.length === 0 || creatingHold) return;

    setCreatingHold(true);
    try {
      const hold = await seatHoldApi.create({
        tripId,
        seatIds: selectedSeats.map((seat) => seat.id),
      });

      // Sang màn chọn phương thức thanh toán (US 6, task của Hạnh) kèm `sessionCode` — phiên
      // giữ chỗ theo khách suốt bước thanh toán. Ghế lấy từ `hold.seatNumbers` (thứ tự chuẩn
      // của máy chủ) thay vì nhãn cục bộ để hai bên không lệch nhau.
      const params = new URLSearchParams({
        tripId,
        sessionCode: hold.sessionCode,
        seats: hold.seatNumbers.join(', '),
      });
      if (routeName) params.set('routeName', routeName);
      if (departureTime) params.set('departureTime', departureTime);
      if (busType) params.set('busType', busType);

      // Ghế chưa có giá thì KHÔNG truyền `total` — thà để màn sau hiện thiếu còn hơn âm thầm
      // coi bằng 0.
      if (totalPrice != null) params.set('total', String(totalPrice));

      navigate(`/payment?${params.toString()}`, { state: { seatHold: hold } });
    } catch (err) {
      const appError = err as AppError;
      message.error(appError.customMessage || 'Không thể giữ ghế. Vui lòng thử lại.');
      syncFromServer();
    } finally {
      setCreatingHold(false);
    }
  };

  // Chưa có tripId → không có chuyến nào để vẽ sơ đồ. Mời quay lại luồng tra cứu.
  if (!tripId) {
    return (
      <EmptyState
        description="Chưa chọn chuyến xe. Hãy quay lại kết quả tìm kiếm để chọn một chuyến rồi chọn ghế."
        action={
          <Button type="primary" onClick={() => navigate('/route-lookup')}>
            Quay lại tra cứu tuyến
          </Button>
        }
      />
    );
  }

  // Gọi API hỏng → trạng thái riêng, không mạo nhận là "xe không có ghế".
  if (outcome?.status === 'error') {
    return (
      <ErrorState
        title="Không tải được sơ đồ ghế"
        description={outcome.message}
        onRetry={() => setReloadToken((n) => n + 1)}
      />
    );
  }

  if (!seatMap) {
    return <LoadingState description="Đang tải sơ đồ ghế…" />;
  }

  // Chuyến có thật nhưng xe chưa được cấu hình sơ đồ ghế — hợp đồng chốt đây là 200 với
  // floors: 0 và seats: [] (trạng thái dữ liệu bình thường, xem docs/api-contract.md). Không
  // để lưới trống vì trông như màn hình hỏng, cũng không dùng ErrorState vì không phải lỗi tải.
  if (seatMap.seats.length === 0) {
    return (
      <EmptyState
        description="Chuyến này chưa có sơ đồ ghế nên chưa chọn được chỗ ngồi. Hãy chọn chuyến khác hoặc quay lại sau."
        action={
          <Button type="primary" onClick={() => navigate(-1)}>
            Quay lại
          </Button>
        }
      />
    );
  }

  return (
    <div>
      <PageHeader
        title="Chọn ghế ngồi"
        subtitle={
          <>
            {routeCode && (
              <>
                <RouteBadge code={routeCode} size="sm" />{' '}
              </>
            )}
            {subtitleMeta}
          </>
        }
        onBack={() => navigate(-1)}
      />

      <BookingProgress current="seat" />

      <Row gutter={[16, 16]}>
        {/* Sơ đồ ghế — thân xe vẽ dạng lưới, lối đi nằm giữa cột 2 và cột 3. */}
        <Col xs={24} lg={17}>
          <PageCard title="Sơ đồ ghế" extra={<SeatStatusLegend />}>
            <div
              style={{
                background: token.colorFillQuaternary,
                borderRadius: token.borderRadiusLG,
                border: `1px solid ${token.colorBorderSecondary}`,
                padding: 24,
              }}
            >
              {/* Đầu xe — một vạch ngang thay cho biểu tượng; vạch nằm trên cùng nên đọc ra
                  ngay chiều của xe. */}
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  gap: 6,
                  marginBottom: 16,
                }}
              >
                <div
                  style={{ width: 72, height: 4, borderRadius: 2, background: token.colorBorder }}
                />
                <Text type="secondary" style={{ fontSize: 12 }}>
                  Đầu xe · Tài xế
                </Text>
              </div>

              {floorsOf(seatMap).map((floor) => (
                <FloorGrid
                  key={floor}
                  floor={floor}
                  floors={seatMap.floors}
                  seats={seatMap.seats}
                  selectedSeatIds={selectedSeatIds}
                  hoveredSeatId={hoveredSeatId}
                  onHover={setHoveredSeatId}
                  onToggle={toggleSeat}
                />
              ))}
            </div>
          </PageCard>
        </Col>

        {/* Ghế đã chọn + tổng tiền tạm tính + nút đi tiếp. */}
        <Col xs={24} lg={7}>
          <div style={{ position: 'sticky', top: 88 }}>
            <PageCard
              title={
                selectedSeats.length > 0 ? `Ghế đã chọn (${selectedSeats.length})` : 'Ghế đã chọn'
              }
            >
              {selectedSeats.length === 0 ? (
                <Text type="secondary">
                  Chưa chọn ghế nào. Chạm vào ghế trống trên sơ đồ để chọn.
                </Text>
              ) : (
                <>
                  <div style={{ marginBottom: 16 }}>
                    {selectedSeats.map((seat, index) => (
                      <div
                        key={seat.id}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          gap: 8,
                          padding: '8px 0',
                          borderBottom:
                            index === selectedSeats.length - 1
                              ? 'none'
                              : `1px solid ${token.colorBorderSecondary}`,
                        }}
                      >
                        {/* Ô ghế vẽ bằng đúng bộ màu "đang chọn" của sơ đồ — nhìn là đối
                            chiếu được ngay với ghế trên xe. */}
                        <SeatChip seat={seat} />

                        <Space size={4}>
                          <Text className="tabular-nums">
                            {seat.price != null ? formatVnd(seat.price) : 'Chưa có giá'}
                          </Text>
                          <Button
                            type="text"
                            size="small"
                            aria-label={`Bỏ chọn ghế ${seat.seatNumber}`}
                            icon={<CloseOutlined />}
                            onClick={() => toggleSeat(seat)}
                          />
                        </Space>
                      </div>
                    ))}
                  </div>

                  <div style={{ marginBottom: 16 }}>
                    <Text type="secondary">Tổng tiền tạm tính</Text>
                    <div
                      className="tabular-nums"
                      style={{
                        fontSize: 22,
                        fontWeight: 700,
                        // colorPrimaryText (không phải colorPrimary) — màu primary làm CHỮ trên
                        // nền tối chỉ đạt ~2.85:1; token *Text là biến thể đã tính đủ tương phản
                        // ở cả hai chế độ.
                        color: token.colorPrimaryText,
                      }}
                    >
                      {totalPrice != null ? formatVnd(totalPrice) : 'Chưa có giá'}
                    </div>
                  </div>
                </>
              )}

              <Button
                type="primary"
                block
                loading={creatingHold}
                disabled={selectedSeats.length === 0}
                onClick={handleContinue}
              >
                Tiếp tục đặt vé
              </Button>
            </PageCard>
          </div>
        </Col>
      </Row>
    </div>
  );
}

/** Các tầng của xe, tăng dần — xe một tầng trả về đúng `[1]`. */
function floorsOf(seatMap: TripSeatMap): number[] {
  return [...new Set(seatMap.seats.map((seat) => seat.floor))].sort((a, b) => a - b);
}

/** Ô ghế nhỏ trong cột "Ghế đã chọn" — cùng bộ màu "đang chọn" với ô ghế trên sơ đồ
 * (teal, viền đôi nếu VIP) để hai chỗ không bao giờ vẽ lệch nhau. */
function SeatChip({ seat }: { seat: SeatMapSeat }) {
  const { token } = theme.useToken();
  const { mode } = useColorMode();
  const appearance = seatAppearance('Available', token, mode, {
    selected: true,
    vip: seat.seatType === 'Vip',
  });

  return (
    <span
      className="tabular-nums"
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        minWidth: 34,
        height: 26,
        padding: '0 6px',
        borderRadius: token.borderRadiusSM,
        background: appearance.background,
        border: appearance.border,
        boxShadow: appearance.boxShadow,
        color: appearance.color,
        fontSize: 12,
        fontWeight: 700,
      }}
    >
      {seat.seatNumber}
    </span>
  );
}

/** Lưới ghế của MỘT tầng — các hàng xếp từ đầu xe, lối đi nằm giữa. */
function FloorGrid({
  floor,
  floors,
  seats,
  selectedSeatIds,
  hoveredSeatId,
  onHover,
  onToggle,
}: {
  floor: number;
  floors: number;
  seats: SeatMapSeat[];
  selectedSeatIds: ReadonlySet<string>;
  hoveredSeatId: string | null;
  onHover: (seatId: string | null) => void;
  onToggle: (seat: SeatMapSeat) => void;
}) {
  const { token } = theme.useToken();

  // Ghế của tầng này, nhóm theo hàng rồi sắp hàng tăng dần từ đầu xe.
  const rows = useMemo(() => {
    const byRow = new Map<number, SeatMapSeat[]>();
    seats
      .filter((seat) => seat.floor === floor)
      .forEach((seat) => {
        const list = byRow.get(seat.rowIndex) ?? [];
        list.push(seat);
        byRow.set(seat.rowIndex, list);
      });

    return [...byRow.entries()].sort(([a], [b]) => a - b);
  }, [seats, floor]);

  // Số cột của tầng này = cột lớn nhất có ghế; lối đi chia đôi theo đó.
  const maxColumn = seats.reduce(
    (max, seat) => (seat.floor === floor ? Math.max(max, seat.columnIndex) : max),
    0,
  );
  // Cùng công thức với bản xem trước ở màn cấu hình sơ đồ ghế (SeatMap.tsx): chia ceil(n/2)
  // và chỉ vẽ lối đi khi từ 4 cột — sơ đồ 5–6 cột không lệch nhau giữa hai màn.
  const leftCols = Math.ceil(maxColumn / 2);
  const hasAisle = maxColumn >= 4;

  return (
    <div style={{ marginBottom: floors > 1 ? 20 : 0 }}>
      {floors > 1 && (
        <div style={{ textAlign: 'center', marginBottom: 10 }}>
          {/* Nhãn tầng dạng viền đứt — hàng rào tạm của xe hai tầng, không tranh màu với ghế. */}
          <span
            style={{
              display: 'inline-block',
              padding: '1px 10px',
              borderRadius: token.borderRadiusSM,
              border: `1px dashed ${token.colorBorder}`,
              background: token.colorBgContainer,
              color: token.colorTextSecondary,
              fontSize: 12,
              fontWeight: 600,
            }}
          >
            Tầng {floor}
          </span>
        </div>
      )}

      {rows.map(([, rowSeats]) => {
        const sorted = [...rowSeats].sort((a, b) => a.columnIndex - b.columnIndex);
        const left = sorted.filter((seat) => seat.columnIndex <= leftCols);
        const right = sorted.filter((seat) => seat.columnIndex > leftCols);

        return (
          <div
            key={rowSeats[0].rowIndex}
            style={{ display: 'flex', justifyContent: 'center', gap: 8, marginBottom: 8 }}
          >
            {left.map((seat) => (
              <SeatStatusCell
                key={seat.id}
                seat={seat}
                selected={selectedSeatIds.has(seat.id)}
                hovered={hoveredSeatId === seat.id}
                onHover={onHover}
                onToggle={onToggle}
              />
            ))}

            {/* Lối đi giữa xe — khoảng trống ngăn hai dãy ghế; xe dưới 4 cột thì không có. */}
            {hasAisle && <div style={{ width: 20 }} />}

            {right.map((seat) => (
              <SeatStatusCell
                key={seat.id}
                seat={seat}
                selected={selectedSeatIds.has(seat.id)}
                hovered={hoveredSeatId === seat.id}
                onHover={onHover}
                onToggle={onToggle}
              />
            ))}
          </div>
        );
      })}
    </div>
  );
}
