import { useCallback, useEffect, useMemo, useState } from 'react';
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
import { ReloadOutlined, WarningOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import {
  fetchAllTripsForDay,
  fetchRouteOptions,
  fetchTrips,
  findConflictingTrips,
  updateTrip,
  TRIP_STATUS_META,
  TRIP_STATUS_OPTIONS,
} from '../api/tripApi';
import type { Trip, TripRouteOption, TripStatus } from '../api/tripApi';
import { fetchActiveBusOptions } from '../api/busApi';
import type { BusOption } from '../api/busApi';
import type { AppError } from '../api/axiosClient';
import TripAssignmentModal from '../components/TripAssignmentModal';

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
 *
 * Kèm "cảnh báo trực quan khi trùng lịch xe": cột "Cảnh báo" đánh dấu chuyến đang trùng xe,
 * và modal cảnh báo khi chọn xe trùng giờ với chuyến khác.
 */
export default function TripAssignmentPage() {
  const [routes, setRoutes] = useState<TripRouteOption[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string>();
  // Bắt đầu ở trạng thái "đang tải" cho danh sách tuyến — lần load đầu chạy ngay khi mount.
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [date, setDate] = useState<Dayjs>(() => dayjs());
  // Mặc định lọc chuyến "Đã lên lịch" — phân công điều xe chỉ có nghĩa với chuyến chưa chạy.
  const [status, setStatus] = useState<TripStatus | undefined>('Scheduled');

  const [data, setData] = useState<Trip[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách khi bấm nút "Làm mới" hoặc sau khi lưu phân công.
  const [reloadKey, setReloadKey] = useState(0);

  // Toàn bộ chuyến của tuyến trong ngày đang xem — dùng để dò trùng lịch xe, không dùng để
  // vẽ bảng (bảng vẽ từ `data` phân trang phía server).
  const [dayTrips, setDayTrips] = useState<Trip[]>([]);

  const [busOptions, setBusOptions] = useState<BusOption[]>([]);
  const [loadingBuses, setLoadingBuses] = useState(true);

  const [modalOpen, setModalOpen] = useState(false);
  const [assigningTrip, setAssigningTrip] = useState<Trip | null>(null);
  const [submitting, setSubmitting] = useState(false);

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

  // Tải toàn bộ chuyến trong ngày để dò trùng lịch — tách khỏi effect vẽ bảng vì không phụ
  // thuộc vào bộ lọc trạng thái hay phân trang: dò trùng cần nhìn đủ mọi chuyến chiếm chỗ
  // (Scheduled/Running), còn bảng có thể đang lọc hẹp hơn.
  useEffect(() => {
    // Xoá ngay để không hiện nhầm cảnh báo trùng của tuyến/ngày trước đó trong lúc đang tải.
    // oxlint-disable-next-line react/set-state-in-effect
    setDayTrips([]);

    if (!selectedRouteId) return;

    let cancelled = false;
    const { from, to } = dayBounds(date);

    fetchAllTripsForDay(selectedRouteId, from, to)
      .then((all) => {
        if (!cancelled) setDayTrips(all);
      })
      .catch(() => {
        // Dò trùng là phụ trợ — lỗi thì để trống, bảng chính tự báo lỗi ở effect riêng.
        if (!cancelled) setDayTrips([]);
      });

    return () => {
      cancelled = true;
    };
  }, [selectedRouteId, date, reloadKey]);

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
        setData(result.items);
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

  // Ánh xạ id chuyến → các chuyến trùng lịch xe với nó trong ngày. Chỉ tính chuyến đang chiếm
  // chỗ (Scheduled/Running): chuyến đã huỷ/hoàn thành không chặn chỗ trên thời gian biểu nữa.
  const conflictsByTrip = useMemo(() => {
    const map = new Map<string, Trip[]>();
    for (const trip of dayTrips) {
      if (trip.status !== 'Scheduled' && trip.status !== 'Running') continue;
      const conflicts = findConflictingTrips(dayTrips, trip.busId, trip, trip.id, (t) => t.busId);
      if (conflicts.length > 0) map.set(trip.id, conflicts);
    }
    return map;
  }, [dayTrips]);

  const columns: TableProps<Trip>['columns'] = [
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
      title: 'Cảnh báo',
      key: 'conflict',
      width: 120,
      render: (_, trip) => {
        const conflicts = conflictsByTrip.get(trip.id);
        if (!conflicts || conflicts.length === 0) {
          return <Text type="secondary">—</Text>;
        }
        return (
          <Tooltip
            title={
              <div>
                {conflicts.map((other) => (
                  <div key={other.id}>
                    Trùng chuyến {dayjs(other.departureTime).format('HH:mm')}
                    {other.arrivalTime ? `–${dayjs(other.arrivalTime).format('HH:mm')}` : ''}
                  </div>
                ))}
              </div>
            }
          >
            <Tag color="warning" icon={<WarningOutlined />}>
              Trùng lịch
            </Tag>
          </Tooltip>
        );
      },
    },
    {
      title: 'Tài xế',
      key: 'driver',
      width: 150,
      // Backend chưa trả thông tin tài xế (cột Trips.DriverId chưa có endpoint đọc — task của
      // Kiên/Hiếu), nên cột này tạm hiển thị "Chưa phân công" cho tới khi có API.
      render: () => <Tag color="default">Chưa phân công</Tag>,
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

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Phân công điều xe theo chuyến
        </Title>
        <Text type="secondary">
          Gán xe cho từng chuyến (chọn tài xế sẽ bổ sung khi có API).
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

          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>
        </Space>

        {!selectedRouteId && !loadingRoutes ? (
          <Empty description="Chưa có tuyến nào để phân công điều xe." />
        ) : (
          <Table<Trip>
            rowKey="id"
            columns={columns}
            dataSource={data}
            loading={loading}
            scroll={{ x: 940 }}
            locale={{ emptyText: 'Không có chuyến nào trong ngày này' }}
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
        dayTrips={dayTrips}
        loadingBuses={loadingBuses}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleAssign}
      />
    </div>
  );
}
