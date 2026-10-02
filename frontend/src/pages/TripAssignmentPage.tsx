import { useCallback, useEffect, useState } from 'react';
import type { Key } from 'react';
import {
  Button,
  Card,
  DatePicker,
  Empty,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd';
import type { TableProps } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import {
  fetchRouteOptions,
  fetchTrips,
  updateTrip,
  TRIP_STATUS_META,
  TRIP_STATUS_OPTIONS,
} from '../api/tripApi';
import type { Trip, TripRouteOption, TripStatus } from '../api/tripApi';
import { fetchActiveBusOptions } from '../api/busApi';
import type { BusOption } from '../api/busApi';
import type { AppError } from '../api/axiosClient';
import TripAssignmentModal from '../components/TripAssignmentModal';
import {
  bulkAssignDriver,
  enrichTripsWithDriver,
  fetchDriverOptions,
} from '../api/tripAssignmentApi';
import type { AssignableTrip, DriverOption } from '../api/tripAssignmentApi';
import TripBulkAssignBar from '../components/TripBulkAssignBar';
import UnassignedTripsFilter from '../components/UnassignedTripsFilter';

const { Title, Text } = Typography;

/**
 * Múi giờ Việt Nam (UTC+7), cố định quanh năm — không đổi theo DST.
 * Cùng lối TripListByDayPage: hợp đồng `from`/`to` nhận ISO 8601 CÓ KÈM múi giờ, nên nối
 * hẳn offset vào chuỗi thay vì nhờ `dayjs().toISOString()`.
 */
const VIETNAM_OFFSET = '+07:00';

/** Cặp mốc "trọn ngày" của một ngày theo giờ Việt Nam — xem chú thích ở TripListByDayPage. */
function dayBounds(day: Dayjs): { from: string; to: string } {
  const date = day.format('YYYY-MM-DD');
  return {
    from: `${date}T00:00:00${VIETNAM_OFFSET}`,
    to: `${date}T23:59:59${VIETNAM_OFFSET}`,
  };
}

/**
 * Màn hình phân công điều xe theo chuyến (story 14, task của Nguyễn Đình Băng).
 *
 * Chọn tuyến + ngày + trạng thái rồi gán/đổi XE cho từng chuyến qua PUT /routes/{routeId}/trips/{id}.
 * Phần "chọn tài xế" đã vẽ trong modal nhưng tạm khoá — endpoint gán tài xế (Kiên) và hồ sơ
 * tài xế (Hiếu) chưa có, sẽ nối sau. Phần "phụ xe" bỏ theo quy ước A8.4 (không có vai trò phụ xe).
 */
export default function TripAssignmentPage() {
  const [routes, setRoutes] = useState<TripRouteOption[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string>();
  // Bắt đầu ở trạng thái "đang tải" cho danh sách tuyến — lần load đầu chạy ngay khi mount.
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [date, setDate] = useState<Dayjs>(() => dayjs());
  // Mặc định lọc chuyến "Đã lên lịch" — phân công điều xe chỉ có nghĩa với chuyến chưa chạy.
  const [status, setStatus] = useState<TripStatus | undefined>('Scheduled');

  const [data, setData] = useState<AssignableTrip[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách khi bấm nút "Làm mới" hoặc sau khi lưu phân công.
  const [reloadKey, setReloadKey] = useState(0);

  const [busOptions, setBusOptions] = useState<BusOption[]>([]);
  const [loadingBuses, setLoadingBuses] = useState(true);

  const [modalOpen, setModalOpen] = useState(false);
  const [assigningTrip, setAssigningTrip] = useState<Trip | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Trạng thái cho phần phân công tài xế (task 120): danh sách tài xế cho ô chọn, bộ lọc
  // "chỉ chưa phân công" và các chuyến đang chọn để gán hàng loạt.
  const [driverOptions, setDriverOptions] = useState<DriverOption[]>([]);
  const [loadingDrivers, setLoadingDrivers] = useState(true);
  const [onlyUnassigned, setOnlyUnassigned] = useState(false);
  const [selectedTripIds, setSelectedTripIds] = useState<Key[]>([]);
  const [bulkSubmitting, setBulkSubmitting] = useState(false);

  // Tải danh sách tuyến cho ô chọn, tự chọn tuyến đầu tiên khi mở trang — cùng khuôn
  // TripListByDayPage.
  const loadRoutes = useCallback(async () => {
    try {
      const list = await fetchRouteOptions();
      setRoutes(list);
      setSelectedRouteId((current) => current ?? list[0]?.id);
    } catch (error) {
      message.error((error as Error).message || 'Không tải được danh sách tuyến.');
    } finally {
      setLoadingRoutes(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadRoutes();
  }, [loadRoutes]);

  // Tải danh sách xe đang khai thác cho ô "chọn xe" trong modal — chỉ tải một lần khi mở trang.
  const loadBusOptions = useCallback(async () => {
    try {
      setBusOptions(await fetchActiveBusOptions());
    } catch (error) {
      message.error((error as Error).message || 'Không tải được danh sách xe.');
    } finally {
      setLoadingBuses(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadBusOptions();
  }, [loadBusOptions]);

  // Tải danh sách tài xế đang hoạt động cho ô chọn "phân công hàng loạt" — chỉ tải một lần
  // khi mở trang. Hiện lấy từ nhánh dữ liệu giả (chờ backend task 113 của Kiên).
  const loadDriverOptions = useCallback(async () => {
    try {
      setDriverOptions(await fetchDriverOptions());
    } catch (error) {
      message.error((error as Error).message || 'Không tải được danh sách tài xế.');
    } finally {
      setLoadingDrivers(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadDriverOptions();
  }, [loadDriverOptions]);

  useEffect(() => {
    if (!selectedRouteId) return;

    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API nên tắt cảnh báo
    // react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);

    const { from, to } = dayBounds(date);

    fetchTrips(selectedRouteId, { from, to, status, page, pageSize })
      .then((result) => {
        if (cancelled) return;
        // Gắn thêm thông tin tài xế (driverId/driverName) vào từng chuyến — nhánh giả hiện
        // so le có/không để demo bộ lọc "chỉ chưa phân công"; khi Kiên xong task 113 thì
        // TripResponse tự mang sẵn và hàm này chỉ là chuyển kiểu.
        setData(enrichTripsWithDriver(result.items));
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách chuyến.');
        setData([]);
        setTotal(0);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [selectedRouteId, date, status, page, pageSize, reloadKey]);

  const handleRefresh = () => setReloadKey((key) => key + 1);

  const openAssign = (trip: Trip) => {
    setAssigningTrip(trip);
    setModalOpen(true);
  };

  const handleAssign = async (busId: string) => {
    if (!selectedRouteId || !assigningTrip) return;
    setSubmitting(true);
    try {
      // PUT /trips là sửa toàn phần với busId + departureTime bắt buộc — gửi kèm đủ giờ chạy
      // và trạng thái hiện tại của chuyến để chỉ đổi xe mà không làm mất dữ liệu.
      await updateTrip(selectedRouteId, assigningTrip.id, {
        busId,
        departureTime: assigningTrip.departureTime,
        arrivalTime: assigningTrip.arrivalTime,
        status: assigningTrip.status,
      });
      message.success('Đã cập nhật xe cho chuyến.');
      setModalOpen(false);
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      message.error(appError.customMessage || 'Cập nhật phân công thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  // Phân công tài xế HÀNG LOẠT cho các chuyến đang chọn — task 120. Hiện gọi nhánh giả
  // (bulkAssignDriver ghi vào bản đồ phiên) nên kết quả "dính" sau khi reload; khi Kiên
  // xong task 113 thì hàm gọi endpoint thật và hành vi giữ nguyên.
  const handleBulkAssign = async (driverId: string) => {
    if (!selectedRouteId || selectedTripIds.length === 0) return;
    setBulkSubmitting(true);
    try {
      const count = await bulkAssignDriver(
        selectedRouteId,
        selectedTripIds.map(String),
        driverId,
      );
      message.success(`Đã phân công tài xế cho ${count} chuyến.`);
      setSelectedTripIds([]);
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      message.error(appError.customMessage || 'Phân công hàng loạt thất bại.');
    } finally {
      setBulkSubmitting(false);
    }
  };

  const columns: TableProps<AssignableTrip>['columns'] = [
    {
      title: 'Giờ khởi hành',
      dataIndex: 'departureTime',
      key: 'departureTime',
      width: 120,
      render: (departureTime: string) => (
        <span style={{ fontWeight: 600 }}>{dayjs(departureTime).format('HH:mm')}</span>
      ),
    },
    {
      title: 'Giờ đến',
      dataIndex: 'arrivalTime',
      key: 'arrivalTime',
      width: 100,
      render: (arrivalTime: string | null) =>
        arrivalTime ? dayjs(arrivalTime).format('HH:mm') : '—',
    },
    {
      title: 'Biển số xe',
      dataIndex: 'busLicensePlate',
      key: 'busLicensePlate',
      width: 140,
      render: (plate: string) => (plate ? <Tag>{plate}</Tag> : '—'),
    },
    {
      title: 'Tài xế',
      key: 'driver',
      width: 150,
      // driverName/driverId do enrichTripsWithDriver gắn (nhánh giả). Khi Kiên xong task 113,
      // TripResponse tự mang sẵn và cột này đọc trực tiếp mà không cần lớp làm giàu.
      render: (_, trip) =>
        trip.driverName ? (
          <Tag color="geekblue">{trip.driverName}</Tag>
        ) : (
          <Tag color="default">Chưa phân công</Tag>
        ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 140,
      render: (tripStatus: TripStatus) => {
        // Backend chỉ trả bốn mã. Lỡ có giá trị lạ thì hiện nguyên văn, không để màn hình vỡ.
        const meta = TRIP_STATUS_META[tripStatus];
        return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{tripStatus}</Tag>;
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 120,
      render: (_, trip) => {
        // Chỉ phân công chuyến chưa chạy (Scheduled). Đổi xe giữa chừng (sự cố) là task
        // "API đổi xe/đổi tài xế khi có sự cố" của Hoàng, endpoint riêng chưa có.
        if (trip.status !== 'Scheduled') {
          return (
            <Tooltip title="Chỉ phân công chuyến đã lên lịch (Scheduled)">
              {/* span bọc để tooltip vẫn hiện khi nút bị disabled. */}
              <span>
                <Button type="link" size="small" disabled>
                  Phân công
                </Button>
              </span>
            </Tooltip>
          );
        }

        return (
          <Button type="link" size="small" onClick={() => openAssign(trip)}>
            Phân công
          </Button>
        );
      },
    },
  ];

  // Chỉ hiện chuyến chưa phân công khi bật bộ lọc — lọc phía client trên trang hiện tại
  // (chờ backend thêm tham số `unassigned` ở task 113, khi đó chuyển sang lọc server-side).
  const visibleData = onlyUnassigned ? data.filter((trip) => trip.driverId === null) : data;

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Phân công điều xe theo chuyến
        </Title>
        <Text type="secondary">
          Gán xe cho từng chuyến + phân công tài xế hàng loạt (phần tài xế đang dùng dữ liệu
          giả, chờ API gán tài xế của Kiên).
        </Text>
      </div>

      <Card variant="borderless" style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}>
        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Select
            placeholder="Chọn tuyến"
            style={{ minWidth: 280 }}
            value={selectedRouteId}
            loading={loadingRoutes}
            onChange={(value) => {
              setSelectedRouteId(value);
              setPage(1);
            }}
            options={routes.map((route) => ({
              value: route.id,
              label: `${route.code} — ${route.name}`,
            }))}
          />

          <DatePicker
            value={date}
            format="DD/MM/YYYY"
            allowClear={false}
            onChange={(value) => {
              setDate(value ?? dayjs());
              setPage(1);
            }}
          />

          <Select<TripStatus>
            allowClear
            placeholder="Trạng thái"
            style={{ width: 200 }}
            value={status}
            options={TRIP_STATUS_OPTIONS}
            onChange={(value) => {
              setStatus(value ?? undefined);
              setPage(1);
            }}
          />

          <UnassignedTripsFilter
            value={onlyUnassigned}
            onChange={(value) => {
              setOnlyUnassigned(value);
              setSelectedTripIds([]);
            }}
          />

          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>
        </Space>

        <TripBulkAssignBar
          selectedCount={selectedTripIds.length}
          drivers={driverOptions}
          loadingDrivers={loadingDrivers}
          submitting={bulkSubmitting}
          onClear={() => setSelectedTripIds([])}
          onAssign={handleBulkAssign}
        />

        {!selectedRouteId && !loadingRoutes ? (
          <Empty description="Chưa có tuyến nào để phân công điều xe." />
        ) : (
          <Table<AssignableTrip>
            rowKey="id"
            columns={columns}
            dataSource={visibleData}
            loading={loading}
            scroll={{ x: 820 }}
            locale={{ emptyText: 'Không có chuyến nào trong ngày này' }}
            rowSelection={{
              selectedRowKeys: selectedTripIds,
              onChange: (keys) => setSelectedTripIds(keys),
              getCheckboxProps: (record) => ({
                // Chỉ chọn được chuyến đã lên lịch VÀ chưa có tài xế — phân công hàng loạt
                // chỉ có nghĩa với chuyến chưa phân công.
                disabled: record.status !== 'Scheduled' || record.driverId !== null,
              }),
            }}
            pagination={{
              current: page,
              pageSize,
              total,
              showSizeChanger: true,
              showQuickJumper: true,
              pageSizeOptions: [10, 20, 50],
              showTotal: (t, range) => `Hiển thị ${range[0]}–${range[1]} trên ${t} chuyến`,
              onChange: (nextPage, nextPageSize) => {
                // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
                setPage(nextPageSize !== pageSize ? 1 : nextPage);
                setPageSize(nextPageSize);
              },
            }}
          />
        )}
      </Card>

      <TripAssignmentModal
        open={modalOpen}
        trip={assigningTrip}
        busOptions={busOptions}
        loadingBuses={loadingBuses}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleAssign}
      />
    </div>
  );
}
