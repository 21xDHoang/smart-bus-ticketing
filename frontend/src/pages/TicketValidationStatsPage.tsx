import { useEffect, useState } from 'react';
import { Button, Card, Flex, Progress, Select, Table, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import ticketValidationStatsApi from '../api/ticketValidationStatsApi';
import type {
  ResultCount,
  TicketValidationResult,
  TripOption,
  TripValidationStats,
} from '../api/ticketValidationStatsApi';
import { getTicketValidationReasonMeta } from '../api/ticketValidationApi';
import type { AppError } from '../api/axiosClient';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { formatTime } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả tải danh sách chuyến — ba trạng thái riêng, không gộp. */
type TripsState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; trips: TripOption[] };

/** Kết quả tải bảng thống kê của một chuyến — ba trạng thái riêng, không gộp. */
type StatsState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; stats: TripValidationStats };

/** Nhãn + màu cho một kết quả soát — `Valid` tự khai, năm lý do từ chối lấy lại của màn quét. */
function resultMeta(result: TicketValidationResult) {
  if (result === 'Valid') return { label: 'Hợp lệ — đã lên xe', color: 'green' };
  return getTicketValidationReasonMeta(result);
}

/** Thẻ số liệu — nhãn + giá trị lớn + dòng ghi chú (nếu có). */
function StatTile({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <Card variant="borderless" styles={{ body: { padding: 20 } }}>
      <Text type="secondary">{label}</Text>
      <div style={{ fontSize: 28, fontWeight: 700, lineHeight: 1.2, marginTop: 8 }}>{value}</div>
      {note && (
        <div style={{ marginTop: 8, minHeight: 20 }}>
          <Text type="secondary" style={{ fontSize: 13 }}>
            {note}
          </Text>
        </div>
      )}
    </Card>
  );
}

const BREAKDOWN_COLUMNS: TableProps<ResultCount>['columns'] = [
  {
    title: 'Kết quả',
    dataIndex: 'result',
    key: 'result',
    render: (result: TicketValidationResult) => {
      const meta = resultMeta(result);
      return <Tag color={meta.color}>{meta.label}</Tag>;
    },
  },
  {
    title: 'Số lượt',
    dataIndex: 'count',
    key: 'count',
    align: 'right',
    width: 100,
    render: (count: number) => (
      <Text strong style={{ fontVariantNumeric: 'tabular-nums' }}>
        {count.toLocaleString('vi-VN')}
      </Text>
    ),
  },
  {
    title: 'Tỷ lệ',
    dataIndex: 'pct',
    key: 'pct',
    width: 240,
    render: (pct: number, record) => (
      <Progress percent={pct} status={record.result === 'Valid' ? 'success' : 'normal'} />
    ),
  },
];

// Màn hình "Thống kê lượt soát theo chuyến" (US 15 "Soát vé QR", Sprint 4, dòng 24, task của
// Dương Thị Hạnh) — màn quản trị/vận hành, chỉ Admin và Manager vào được (route guard ở
// App.tsx). Chọn một chuyến rồi xem lượt soát vé của chuyến đó phân bố thế nào: tổng / hợp
// lệ / từng lý do từ chối. Dòng 24 đếm LƯỢT SOÁT (kể cả lượt bị từ chối), không đếm vé.
// Backend endpoint này chưa có nên màn hình đang chạy dữ liệu giả (ticketValidationStatsApi.ts).
export default function TicketValidationStatsPage() {
  const [tripsState, setTripsState] = useState<TripsState>({ status: 'loading' });
  const [selectedTripId, setSelectedTripId] = useState<string | null>(null);
  const [statsState, setStatsState] = useState<StatsState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  // Tải danh sách chuyến một lần rồi chọn sẵn chuyến đầu tiên.
  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API — là lượt tải thật nên tắt cảnh báo
    // react/set-state-in-effect cho đúng ngữ cảnh (cùng lối FillRateReportPage).
    // oxlint-disable-next-line react/set-state-in-effect
    setTripsState({ status: 'loading' });

    ticketValidationStatsApi
      .getTrips()
      .then((trips) => {
        if (cancelled) return;
        setTripsState({ status: 'done', trips });
        setSelectedTripId((current) => current ?? trips[0]?.tripId ?? null);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setTripsState({
          status: 'error',
          message: appError.customMessage || 'Không tải được danh sách chuyến.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  // Tải bảng thống kê khi chuyến được chọn thay đổi (hoặc bấm Làm mới).
  useEffect(() => {
    if (!selectedTripId) return;

    let cancelled = false;

    // oxlint-disable-next-line react/set-state-in-effect
    setStatsState({ status: 'loading' });

    ticketValidationStatsApi
      .getStats(selectedTripId)
      .then((stats) => {
        if (cancelled) return;
        setStatsState({ status: 'done', stats });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setStatsState({
          status: 'error',
          message: appError.customMessage || 'Không tải được số liệu thống kê.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [selectedTripId, reloadKey]);

  return (
    <div>
      <PageHeader
        title="Thống kê lượt soát theo chuyến"
        subtitle="Chọn một chuyến để xem lượt soát vé phân bố thế nào — tổng, hợp lệ và từng lý do từ chối."
      />

      {tripsState.status === 'loading' ? (
        <LoadingState description="Đang tải danh sách chuyến…" />
      ) : tripsState.status === 'error' ? (
        <ErrorState
          title="Không tải được danh sách chuyến"
          description={tripsState.message}
          onRetry={() => setReloadKey((key) => key + 1)}
        />
      ) : tripsState.trips.length === 0 ? (
        <EmptyState description="Hôm nay không có chuyến nào." />
      ) : (
        <>
          {/* Bộ chọn chuyến — nằm trên mọi thẻ và bảng để tất cả cùng đổi theo. */}
          <Flex align="center" gap={12} wrap style={{ marginBottom: 16 }}>
            <Text type="secondary">Chuyến:</Text>
            <Select
              style={{ minWidth: 360 }}
              value={selectedTripId}
              onChange={(value) => setSelectedTripId(value)}
              options={tripsState.trips.map((trip) => ({
                value: trip.tripId,
                label: `${trip.tripCode} · ${trip.routeName} · ${formatTime(trip.departureTime)}`,
              }))}
            />
            <Button icon={<ReloadOutlined />} onClick={() => setReloadKey((key) => key + 1)}>
              Làm mới
            </Button>
          </Flex>

          {statsState.status === 'loading' ? (
            <LoadingState description="Đang tải số liệu thống kê…" />
          ) : statsState.status === 'error' ? (
            <ErrorState
              title="Không tải được số liệu thống kê"
              description={statsState.message}
              onRetry={() => setReloadKey((key) => key + 1)}
            />
          ) : (
            <>
              {/* Dải thẻ số liệu — tự co về 2 cột/1 cột khi màn hình hẹp. */}
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
                  gap: 16,
                  marginBottom: 16,
                }}
              >
                <StatTile
                  label="Tổng lượt soát"
                  value={statsState.stats.totalScans.toLocaleString('vi-VN')}
                  note="Mọi lượt quét, kể cả lượt bị từ chối"
                />
                <StatTile
                  label="Hợp lệ — đã lên xe"
                  value={statsState.stats.validScans.toLocaleString('vi-VN')}
                  note="Result = Valid"
                />
                <StatTile
                  label="Bị từ chối"
                  value={statsState.stats.rejectedScans.toLocaleString('vi-VN')}
                  note="Năm lý do từ chối cộng lại"
                />
                <StatTile
                  label="Tỷ lệ hợp lệ"
                  value={`${statsState.stats.validRatePct}%`}
                  note="Hợp lệ trên tổng lượt soát"
                />
              </div>

              <PageCard
                title={
                  <span>
                    <RouteBadge
                      code={statsState.stats.trip.routeCode}
                      size="sm"
                      style={{ marginRight: 6 }}
                    />
                    {statsState.stats.trip.routeName}
                  </span>
                }
                extra={
                  <Text type="secondary">
                    {formatTime(statsState.stats.trip.departureTime)} ·{' '}
                    {statsState.stats.trip.busPlate}
                  </Text>
                }
              >
                <Table<ResultCount>
                  rowKey="result"
                  columns={BREAKDOWN_COLUMNS}
                  dataSource={statsState.stats.byResult}
                  pagination={false}
                  scroll={{ x: 560 }}
                />
              </PageCard>
            </>
          )}
        </>
      )}
    </div>
  );
}
