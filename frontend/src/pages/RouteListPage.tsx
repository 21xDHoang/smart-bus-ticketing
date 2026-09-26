import { useEffect, useState } from 'react';
import { Card, Input, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { fetchRoutes, ROUTE_STATUS_META, ROUTE_STATUS_OPTIONS } from '../api/routeApi';
import type { Route, RouteStatus } from '../api/routeApi';
import type { AppError } from '../api/axiosClient';

// Màn hình chỉ ĐỌC: bảng + tìm kiếm + lọc trạng thái + phân trang (story 12).
// Nút Thêm/Sửa và xác nhận xoá thuộc task "Form Thêm/Sửa tuyến đường + xác nhận xoá"
// của Dương Thị Hạnh — chưa gắn vào đây để hai task không giẫm lên nhau.
const columns: TableProps<Route>['columns'] = [
  {
    title: 'Mã tuyến',
    dataIndex: 'code',
    key: 'code',
    width: 110,
    render: (code: string) => <Tag color="blue">{code}</Tag>,
  },
  {
    title: 'Tuyến đường',
    key: 'name',
    render: (_, route) => (
      <div>
        <div style={{ fontWeight: 600 }}>{route.name}</div>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {route.origin} → {route.destination}
        </Typography.Text>
      </div>
    ),
  },
  {
    title: 'Độ dài',
    dataIndex: 'distanceKm',
    key: 'distanceKm',
    width: 110,
    render: (distanceKm: number) => `${distanceKm} km`,
  },
  {
    title: 'Trạng thái',
    dataIndex: 'status',
    key: 'status',
    width: 170,
    render: (status: RouteStatus) => {
      // Backend chỉ trả 'Active' | 'Inactive'. Lỡ có giá trị lạ thì hiện nguyên văn,
      // không để màn hình vỡ vì tra meta không thấy.
      const meta = ROUTE_STATUS_META[status];
      return meta ? <Tag color={meta.color}>{meta.label}</Tag> : <Tag>{status}</Tag>;
    },
  },
  {
    title: 'Ngày tạo',
    dataIndex: 'createdAt',
    key: 'createdAt',
    width: 130,
    render: (createdAt: string) => dayjs(createdAt).format('DD/MM/YYYY'),
  },
];

const RouteListPage = () => {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<RouteStatus>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<Route[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchRoutes({ page, pageSize, search: search || undefined, status })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách tuyến đường.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [search, status, page, pageSize]);

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Typography.Title level={4} style={{ margin: 0 }}>
          Danh sách tuyến đường
        </Typography.Title>
        <Typography.Text type="secondary">
          Tra cứu tuyến theo mã, tên, điểm đi — điểm đến và trạng thái khai thác.
        </Typography.Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Input.Search
            allowClear
            placeholder="Tìm theo mã, tên hoặc điểm đi — điểm đến"
            style={{ width: 320 }}
            onSearch={(value) => {
              setSearch(value.trim());
              setPage(1);
            }}
            onChange={(e) => {
              // Bấm nút X (allowClear) thì cập nhật ngay, không cần chờ Enter.
              if (!e.target.value) {
                setSearch('');
                setPage(1);
              }
            }}
          />

          <Select<RouteStatus>
            allowClear
            placeholder="Trạng thái"
            style={{ width: 200 }}
            options={ROUTE_STATUS_OPTIONS}
            onChange={(value) => {
              setStatus(value ?? undefined);
              setPage(1);
            }}
          />
        </Space>

        <Table<Route>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 720 }}
          locale={{ emptyText: 'Không tìm thấy tuyến đường nào' }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showQuickJumper: true,
            pageSizeOptions: [10, 20, 50],
            showTotal: (t, range) => `Hiển thị ${range[0]}–${range[1]} trên ${t} tuyến`,
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          }}
        />
      </Card>
    </div>
  );
};

export default RouteListPage;
