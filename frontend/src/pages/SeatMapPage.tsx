import { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Button, Col, Row, Space, Tag, Typography, theme } from 'antd';
import { CloseOutlined } from '@ant-design/icons';
import seatMapApi from '../api/seatMapApi';
import type { SeatMapSeat, TripSeatMap } from '../api/seatMapApi';
import type { AppError } from '../api/axiosClient';
import { formatVnd, formatTime } from '../components/ui/format';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader } from '../components/ui';
import { isSeatSelectable } from '../components/seatStatus';
import SeatStatusCell from '../components/SeatStatusCell';
import SeatStatusLegend from '../components/SeatStatusLegend';

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
export default function SeatMapPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const { token } = theme.useToken();

  const tripId = searchParams.get('tripId')?.trim() ?? '';
  const routeName = searchParams.get('routeName')?.trim() ?? '';
  const departureTime = searchParams.get('departureTime')?.trim() ?? '';
  const fallbackBusType = searchParams.get('busType')?.trim() ?? '';

  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [selectedSeatIds, setSelectedSeatIds] = useState<ReadonlySet<string>>(new Set());
  const [hoveredSeatId, setHoveredSeatId] = useState<string | null>(null);

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

  const handleContinue = () => {
    const labels = selectedSeats.map((seat) => seat.seatNumber).join(', ');

    // Sang màn hình chờ kết quả thanh toán (US 6, Sprint 3 — task của Nguyễn Đình Băng).
    // Đây là lối tắt DEMO để chạm được màn hình chờ ngay khi các bước trung gian (giữ chỗ của
    // Kiên/Thịnh/Hạnh, chọn phương thức thanh toán của Hạnh) chưa nối xong. Khi các bước đó có,
    // thay `paymentCode` giả bằng mã thanh toán thật do backend tạo giao dịch trả về.
    const params = new URLSearchParams({
      paymentCode: `demo-${tripId}`,
      amount: String(totalPrice ?? 0),
      seats: labels,
    });

    navigate(`/payment-waiting?${params.toString()}`);
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
        subtitle={[
          routeName,
          departureTime ? formatTime(departureTime) : '',
          busType,
        ]
          .filter(Boolean)
          .join(' · ')}
        onBack={() => navigate(-1)}
      />

      <Row gutter={[16, 16]}>
        {/* Sơ đồ ghế — thân xe vẽ dạng lưới, lối đi nằm giữa cột 2 và cột 3. */}
        <Col xs={24} lg={17}>
          <PageCard
            title="Sơ đồ ghế"
            extra={<SeatStatusLegend />}
          >
            <div
              style={{
                background: token.colorFillQuaternary,
                borderRadius: token.borderRadiusLG,
                border: `1px solid ${token.colorBorderSecondary}`,
                padding: 24,
              }}
            >
              <div style={{ textAlign: 'center', marginBottom: 16 }}>
                <Text type="secondary">🚌 Đầu xe · Tài xế</Text>
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
            <PageCard title="Ghế đã chọn">
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
                        <Space size={6}>
                          <Text strong>{seat.seatNumber}</Text>
                          {seat.seatType === 'Vip' && (
                            <Tag color="gold" style={{ marginInlineEnd: 0 }}>
                              VIP
                            </Tag>
                          )}
                        </Space>

                        <Space size={4}>
                          <Text>{seat.price != null ? formatVnd(seat.price) : 'Chưa có giá'}</Text>
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
                    <div style={{ fontSize: 22, fontWeight: 700, color: token.colorPrimary }}>
                      {totalPrice != null ? formatVnd(totalPrice) : 'Chưa có giá'}
                    </div>
                  </div>
                </>
              )}

              <Button
                type="primary"
                block
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

  return (
    <div style={{ marginBottom: floors > 1 ? 20 : 0 }}>
      {floors > 1 && (
        <div style={{ textAlign: 'center', marginBottom: 10 }}>
          <Tag color="blue">Tầng {floor}</Tag>
        </div>
      )}

      {rows.map(([, rowSeats]) => {
        const sorted = [...rowSeats].sort((a, b) => a.columnIndex - b.columnIndex);
        const left = sorted.filter((seat) => seat.columnIndex <= 2);
        const right = sorted.filter((seat) => seat.columnIndex >= 3);

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

            {/* Lối đi giữa xe — khoảng trống ngăn hai dãy ghế. */}
            <div style={{ width: 20 }} />

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
