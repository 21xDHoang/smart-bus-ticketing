import { useCallback, useEffect, useState } from 'react';
import {
  Button,
  Card,
  DatePicker,
  Empty,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd';
import type { TableProps } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import {
  fetchRouteOptions,
  fetchTrips,
  TRIP_STATUS_META,
  TRIP_STATUS_OPTIONS,
} from '../api/tripApi';
import type { Trip, TripListParams, TripRouteOption, TripStatus } from '../api/tripApi';
import {
  cancelTrip,
  createTrip,
  fetchActiveBuses,
  updateTrip,
} from '../api/tripScheduleApi';
import type { BusOption, CreateTripPayload, UpdateTripPayload } from '../api/tripScheduleApi';
import type { AppError } from '../api/axiosClient';
import TripScheduleFormModal from '../components/TripScheduleFormModal';

const { Title, Text } = Typography;

/** Múi giờ Việt Nam cố định — cùng lối dayBounds của TripListByDayPage. */
const VIETNAM_OFFSET = '+07:00';

/** Cặp mốc "trọn ngày" theo giờ Việt Nam cho bộ lọc ngày (tính luôn mốc — inclusive). */
function dayBounds(day: Dayjs): { from: string; to: string } {
  const date = day.format('YYYY-MM-DD');
  return {
    from: `${date}T00:00:00${VIETNAM_OFFSET}`,
    to: `${date}T23:59:59${VIETNAM_OFFSET}`,
  };
}

// Màn hình quản lý lịch trình (story 13, task của Dương Thị Hạnh): chọn tuyến, xem các
// chuyến trong ngày, thêm/sửa/huỷ chuyến. Khác màn hình "Danh sách chuyến theo ngày"
// (chỉ đọc) ở chỗ có đủ thao tác ghi qua RouteTripsController.
export default function TripSchedulePage() {
  const [routes, setRoutes] = useState<TripRouteOption[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string>();
  // Bắt đầu ở trạng thái "đang tải" cho danh sách tuyến — lần load đầu chạy ngay khi mount.
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [buses, setBuses] = useState<BusOption[]>([]);

  const [date, setDate] = useState<Dayjs | null>(() => dayjs());
  const [status, setStatus] = useState<TripStatus>();

  const [data, setData] = useState<Trip[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/huỷ thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Trip | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Tải danh sách tuyến + xe cho hai ô chọn, tự chọn tuyến đầu tiên khi mở trang.
  const loadOptions = useCallback(async () => {
    try {
      const [routeList, busList] = await Promise.all([fetchRouteOptions(), fetchActiveBuses()]);
      setRoutes(routeList);
      setSelectedRouteId((current) => current ?? routeList[0]?.id);
      setBuses(busList);
    } catch (error) {
      message.error((error as Error).message || 'Không tải được dữ liệu ban đầu.');
    } finally {
      setLoadingRoutes(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadOptions();
  }, [loadOptions]);

  // Tải danh sách chuyến mỗi khi tuyến / ngày / trạng thái / phân trang / reloadKey đổi.
  useEffect(() => {
    if (!selectedRouteId) return;

    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);

    const params: TripListParams = { page, pageSize, status };
    if (date) {
      const { from, to } = dayBounds(date);
      params.from = from;
      params.to = to;
    }

    fetchTrips(selectedRouteId, params)
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

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (trip: Trip) => {
    setEditing(trip);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: CreateTripPayload | UpdateTripPayload, id?: string) => {
    if (!selectedRouteId) return;
    setSubmitting(true);
    try {
      if (id) {
        await updateTrip(selectedRouteId, id, payload as UpdateTripPayload);
        message.success('Đã cập nhật chuyến.');
      } else {
        await createTrip(selectedRouteId, payload);
        message.success('Đã thêm chuyến.');
      }
      setModalOpen(false);
      setReloadKey((key) => key + 1);
    } catch (error) {
      const appError = error as AppError;
      // Có lỗi theo từng trường thì để modal gắn vào ô input; ngược lại mới hiện toast.
      if (!appError.errors) {
        message.error(appError.customMessage || 'Thao tác thất bại.');
      }
      throw error;
    } finally {
      setSubmitting(false);
    }
  };

  const handleCancel = async (trip: Trip) => {
    if (!selectedRouteId) return;
    try {
      await cancelTrip(selectedRouteId, trip.id);
      message.success('Đã huỷ chuyến.');
      // Huỷ dòng cuối của trang cuối thì lùi về trang trước — tránh đứng ở trang rỗng (400).
      if (data.length === 1 && page > 1) {
        setPage(page - 1);
      } else {
        setReloadKey((key) => key + 1);
      }
    } catch (error) {
      message.error((error as AppError).customMessage || 'Huỷ chuyến thất bại.');
    }
  };

  const columns: TableProps<Trip>['columns'] = [
    {
      title: 'Khởi hành',
      dataIndex: 'departureTime',
      key: 'departureTime',
      width: 150,
      render: (departureTime: string) => (
        <span style={{ fontWeight: 600 }}>
          {dayjs(departureTime).format('DD/MM/YYYY HH:mm')}
        </span>
      ),
    },
    {
      title: 'Giờ đến',
      dataIndex: 'arrivalTime',
      key: 'arrivalTime',
      width: 110,
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
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 150,
      render: (tripStatus: TripStatus) => {
        // Backend chỉ trả bốn mã trong TRIP_STATUS_META. Lỡ có giá trị lạ thì hiện
        // nguyên văn, không để màn hình vỡ vì tra meta không thấy.
        const meta = TRIP_STATUS_META[tripStatus];
        return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{tripStatus}</Tag>;
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 130,
      render: (_, trip) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(trip)}>
            Sửa
          </Button>
          {/* Chuyến đã huỷ / đã chạy xong không huỷ được nữa — backend chặn Completed bằng
              409, còn huỷ chuyến đã Cancelled là vô nghĩa nên ẩn hẳn nút. */}
          {(trip.status === 'Scheduled' || trip.status === 'Running') && (
            <Popconfirm
              title="Huỷ chuyến?"
              description="Chuyến sẽ chuyển sang trạng thái Đã huỷ."
              okText="Huỷ chuyến"
              cancelText="Giữ lại"
              okButtonProps={{ danger: true }}
              onConfirm={() => handleCancel(trip)}
            >
              <Button type="link" size="small" danger>
                Huỷ
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Quản lý lịch trình
        </Title>
        <Text type="secondary">Thêm, sửa và huỷ chuyến xe của từng tuyến theo ngày.</Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
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
            allowClear
            placeholder="Lọc theo ngày"
            onChange={(value) => {
              // Bỏ chọn ngày (allowClear) = xem mọi ngày, không lọc khoảng.
              setDate(value ?? null);
              setPage(1);
            }}
          />

          <Select<TripStatus>
            allowClear
            placeholder="Trạng thái"
            style={{ width: 200 }}
            options={TRIP_STATUS_OPTIONS}
            onChange={(value) => {
              setStatus(value ?? undefined);
              setPage(1);
            }}
          />

          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>

          <Button
            type="primary"
            icon={<PlusOutlined />}
            disabled={!selectedRouteId}
            onClick={openCreate}
          >
            Thêm chuyến
          </Button>
        </Space>

        {!selectedRouteId && !loadingRoutes ? (
          <Empty description="Chưa có tuyến nào để quản lý lịch trình." />
        ) : (
          <Table<Trip>
            rowKey="id"
            columns={columns}
            dataSource={data}
            loading={loading}
            scroll={{ x: 700 }}
            locale={{ emptyText: 'Không có chuyến nào khớp bộ lọc' }}
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

      <TripScheduleFormModal
        open={modalOpen}
        editing={editing}
        buses={buses}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
}
