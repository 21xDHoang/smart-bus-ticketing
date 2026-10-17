import { useEffect, useState } from 'react';
import { Button, Flex, Select, Table, Typography } from 'antd';
import type { TableProps } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import tripPassengerListApi from '../api/tripPassengerListApi';
import type {
  BoardedPassenger,
  DriverTripOption,
  TripBoardingList,
} from '../api/tripPassengerListApi';
import type { AppError } from '../api/axiosClient';
import { EmptyState, ErrorState, LoadingState, PageCard, PageHeader, RouteBadge } from '../components/ui';
import { PAGE_SIZE_OPTIONS, formatTime } from '../components/ui/format';

const { Text } = Typography;

/** Kết quả tải danh sách chuyến — ba trạng thái riêng, không gộp. */
type TripsState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; trips: DriverTripOption[] };

/** Kết quả tải danh sách khách của một chuyến — ba trạng thái riêng, không gộp. */
type BoardingState =
  | { status: 'loading' }
  | { status: 'error'; message: string }
  | { status: 'done'; data: TripBoardingList };

const COLUMNS: TableProps<BoardedPassenger>['columns'] = [
  {
    title: 'Hành khách',
    dataIndex: 'passengerName',
    key: 'passengerName',
    render: (name: string | null) =>
      name ? <Text strong>{name}</Text> : <Text type="secondary">Chưa có tên</Text>,
  },
  {
    title: 'Ghế',
    dataIndex: 'seatNumber',
    key: 'seatNumber',
    width: 80,
    render: (seat: string) => <Text code>{seat}</Text>,
  },
  {
    title: 'Trạm lên',
    dataIndex: 'boardingStopName',
    key: 'boardingStopName',
    render: (stop: string | null) => stop ?? <Text type="secondary">—</Text>,
  },
  {
    title: 'Giờ lên xe',
    dataIndex: 'boardedAt',
    key: 'boardedAt',
    width: 110,
    render: (at: string) => (
      <span style={{ fontVariantNumeric: 'tabular-nums' }}>{formatTime(at)}</span>
    ),
  },
  {
    title: 'Mã vé',
    dataIndex: 'ticketCode',
    key: 'ticketCode',
    render: (code: string) => <Text code>{code}</Text>,
  },
];

// Màn hình "Hành khách đã lên xe" (US 15 "Soát vé QR", Sprint 4, dòng 22, task của Dương Thị
// Hạnh) — màn của tài xế: chọn chuyến đang chạy rồi xem ai đã được soát vé hợp lệ và lên xe.
// Backend endpoint này chưa có nên màn hình đang chạy dữ liệu giả (tripPassengerListApi.ts),
// cùng lối màn tỷ lệ lấp đầy (fillRateReportApi.ts).
export default function TripPassengerListPage() {
  const [tripsState, setTripsState] = useState<TripsState>({ status: 'loading' });
  const [selectedTripId, setSelectedTripId] = useState<string | null>(null);
  const [boardingState, setBoardingState] = useState<BoardingState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  // Tải danh sách chuyến một lần rồi chọn sẵn chuyến đầu tiên.
  useEffect(() => {
    let cancelled = false;

    // Bật trạng thái đang tải khi bắt đầu gọi API — là lượt tải thật nên tắt cảnh báo
    // react/set-state-in-effect cho đúng ngữ cảnh (cùng lối FillRateReportPage).
    // oxlint-disable-next-line react/set-state-in-effect
    setTripsState({ status: 'loading' });

    tripPassengerListApi
      .getDriverTrips()
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

  // Tải danh sách khách khi chuyến được chọn thay đổi (hoặc bấm Làm mới).
  useEffect(() => {
    if (!selectedTripId) return;

    let cancelled = false;

    // oxlint-disable-next-line react/set-state-in-effect
    setBoardingState({ status: 'loading' });

    tripPassengerListApi
      .getBoardedPassengers(selectedTripId)
      .then((data) => {
        if (cancelled) return;
        setBoardingState({ status: 'done', data });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setBoardingState({
          status: 'error',
          message: appError.customMessage || 'Không tải được danh sách hành khách.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [selectedTripId, reloadKey]);

  return (
    <div>
      <PageHeader
        title="Hành khách đã lên xe"
        subtitle="Chọn chuyến đang chạy để xem ai đã được soát vé hợp lệ và đã lên xe."
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
          {/* Bộ chọn chuyến — nằm trên bảng để đổi chuyến là bảng đổi theo ngay. */}
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

          {boardingState.status === 'loading' ? (
            <LoadingState description="Đang tải danh sách hành khách…" />
          ) : boardingState.status === 'error' ? (
            <ErrorState
              title="Không tải được danh sách hành khách"
              description={boardingState.message}
              onRetry={() => setReloadKey((key) => key + 1)}
            />
          ) : (
            <PageCard
              title={
                <span>
                  <RouteBadge
                    code={boardingState.data.trip.routeCode}
                    size="sm"
                    style={{ marginRight: 6 }}
                  />
                  {boardingState.data.trip.routeName}
                </span>
              }
              extra={
                <Text type="secondary">
                  {formatTime(boardingState.data.trip.departureTime)} ·{' '}
                  {boardingState.data.trip.busPlate} · {boardingState.data.boardedCount} khách
                </Text>
              }
            >
              <Table<BoardedPassenger>
                rowKey="validationId"
                columns={COLUMNS}
                dataSource={boardingState.data.passengers}
                scroll={{ x: 720 }}
                locale={{ emptyText: 'Chưa có hành khách nào lên xe' }}
                pagination={{
                  pageSize: 10,
                  showSizeChanger: true,
                  pageSizeOptions: PAGE_SIZE_OPTIONS,
                  showTotal: (total, range) =>
                    `Hiển thị ${range[0]}–${range[1]} trên ${total} hành khách`,
                }}
              />
            </PageCard>
          )}
        </>
      )}
    </div>
  );
}
