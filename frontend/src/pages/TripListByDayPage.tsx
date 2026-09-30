import { useCallback, useEffect, useState } from 'react';
import {
  Button,
  Card,
  DatePicker,
  Empty,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  message,
} from 'antd';
import type { TableProps } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import {
  fetchRouteOptions,
  fetchTrips,
  TRIP_STATUS_META,
  TRIP_STATUS_OPTIONS,
} from '../api/tripApi';
import type { Trip, TripRouteOption, TripStatus } from '../api/tripApi';
import type { AppError } from '../api/axiosClient';

const { Title, Text } = Typography;

/**
 * Múi giờ Việt Nam (UTC+7), cố định quanh năm — không đổi theo DST.
 *
 * Hợp đồng `from`/`to` của GET /routes/{routeId}/trips nhận ISO 8601 CÓ KÈM múi giờ
 * (docs/api-contract.md mục "Giờ gửi lên phải kèm múi giờ"), nên ta nối hẳn offset vào
 * chuỗi thay vì nhờ `dayjs().toISOString()` — như vậy "ngày" trên lịch luôn là một ngày
 * Việt Nam, không phụ thuộc múi giờ của trình duyệt người xem.
 */
const VIETNAM_OFFSET = '+07:00';

/**
 * Cặp mốc "trọn ngày" của một ngày theo giờ Việt Nam.
 *
 * `day.format('YYYY-MM-DD')` lấy đúng ngày người dùng NHÌN THẤY trên DatePicker. Mốc cuối
 * lùi về `23:59:59` chứ không dùng `00:00:00` của ngày hôm sau: hợp đồng "tính luôn mốc"
 * (inclusive), và chuyến khởi hành đúng 00:00 ngày hôm sau đã thuộc về ngày kế tiếp.
 */
function dayBounds(day: Dayjs): { from: string; to: string } {
  const date = day.format('YYYY-MM-DD');
  return {
    from: `${date}T00:00:00${VIETNAM_OFFSET}`,
    to: `${date}T23:59:59${VIETNAM_OFFSET}`,
  };
}

// Các cột không phụ thuộc state nào (không có nút Sửa/Xoá — màn hình này chỉ đọc), nên
// dựng sẵn ở mức module như BASE_COLUMNS của RouteListPage.
const COLUMNS: TableProps<Trip>['columns'] = [
  {
    title: 'Giờ khởi hành',
    dataIndex: 'departureTime',
    key: 'departureTime',
    width: 130,
    render: (departureTime: string) => (
      <span style={{ fontWeight: 600 }}>{dayjs(departureTime).format('HH:mm')}</span>
    ),
  },
  {
    title: 'Giờ đến',
    dataIndex: 'arrivalTime',
    key: 'arrivalTime',
    width: 110,
    render: (arrivalTime: string | null) => (arrivalTime ? dayjs(arrivalTime).format('HH:mm') : '—'),
  },
  {
    title: 'Biển số xe',
    dataIndex: 'busLicensePlate',
    key: 'busLicensePlate',
    width: 150,
    render: (plate: string) => (plate ? <Tag>{plate}</Tag> : '—'),
  },
  {
    title: 'Trạng thái',
    dataIndex: 'status',
    key: 'status',
    width: 150,
    render: (status: TripStatus) => {
      // Backend chỉ trả bốn mã ở trên. Lỡ có giá trị lạ thì hiện nguyên văn,
      // không để màn hình vỡ vì tra meta không thấy.
      const meta = TRIP_STATUS_META[status];
      return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{status}</Tag>;
    },
  },
];

// Màn hình danh sách chuyến theo ngày (story 13, task của Nguyễn Đình Băng): chọn tuyến,
// chọn ngày, lọc trạng thái rồi xem các chuyến trong ngày đó. Chỉ đọc — phần thêm/sửa/xoá
// lịch trình là màn hình riêng của Dương Thị Hạnh.
export default function TripListByDayPage() {
  const [routes, setRoutes] = useState<TripRouteOption[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string>();
  // Bắt đầu ở trạng thái "đang tải" cho danh sách tuyến — lần load đầu chạy ngay khi mount.
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [date, setDate] = useState<Dayjs>(() => dayjs());
  const [status, setStatus] = useState<TripStatus>();

  const [data, setData] = useState<Trip[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách khi bấm nút "Làm mới".
  const [reloadKey, setReloadKey] = useState(0);

  // Tải danh sách tuyến cho ô chọn, tự chọn tuyến đầu tiên khi mở trang — cùng khuôn
  // FareConfigPage.loadRoutes.
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

  useEffect(() => {
    if (!selectedRouteId) return;

    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
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

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Danh sách chuyến theo ngày
        </Title>
        <Text type="secondary">
          Xem các chuyến xe của một tuyến trong ngày, lọc theo trạng thái.
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
          <Empty description="Chưa có tuyến nào để xem chuyến." />
        ) : (
          <Table<Trip>
            rowKey="id"
            columns={COLUMNS}
            dataSource={data}
            loading={loading}
            scroll={{ x: 560 }}
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
    </div>
  );
}
