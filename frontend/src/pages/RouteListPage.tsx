import { useEffect, useState } from 'react';
import { Button, Card, Input, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { PlusOutlined } from '@ant-design/icons';
import { fetchRoutes, ROUTE_STATUS_META, ROUTE_STATUS_OPTIONS } from '../api/routeApi';
import type { Route, RouteStatus } from '../api/routeApi';
import routeCrudApi from '../api/routeCrudApi';
import type { RoutePayload, UpdateRoutePayload } from '../api/routeCrudApi';
import type { AppError } from '../api/axiosClient';
import RouteFormModal from '../components/RouteFormModal';

// Màn hình danh sách tuyến (story 12): bảng + tìm kiếm + lọc trạng thái + phân trang.
// Phần Thêm/Sửa tuyến + xác nhận xoá (nút, modal, popconfirm) là task "Form Thêm/Sửa
// tuyến đường + xác nhận xoá" của Dương Thị Hạnh, gắn vào đây dùng chung một trang.
const BASE_COLUMNS: TableProps<Route>['columns'] = [
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
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/xoá thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Route | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (route: Route) => {
    setEditing(route);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: RoutePayload | UpdateRoutePayload, id?: string) => {
    setSubmitting(true);
    try {
      if (id) {
        await routeCrudApi.update(id, payload as UpdateRoutePayload);
        message.success('Đã cập nhật tuyến đường.');
      } else {
        await routeCrudApi.create(payload);
        message.success('Đã thêm tuyến đường.');
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

  const handleDelete = async (id: string) => {
    try {
      await routeCrudApi.remove(id);
      message.success('Đã ngừng khai thác tuyến đường.');
      // Xoá dòng cuối của trang cuối thì lùi về trang trước — tránh đứng ở trang rỗng (400).
      if (data.length === 1 && page > 1) {
        setPage(page - 1);
      } else {
        setReloadKey((key) => key + 1);
      }
    } catch (error) {
      message.error((error as AppError).customMessage || 'Thao tác thất bại.');
    }
  };

  // Cột "Hành động" cần gọi openEdit/handleDelete nên ghép ở đây, không để ở module.
  const columns: TableProps<Route>['columns'] = [
    ...BASE_COLUMNS,
    {
      title: 'Hành động',
      key: 'actions',
      width: 140,
      render: (_, route) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(route)}>
            Sửa
          </Button>
          <Popconfirm
            title="Ngừng khai thác tuyến?"
            description={`Tuyến “${route.name}” sẽ chuyển sang trạng thái ngừng khai thác.`}
            okText="Ngừng"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(route.id)}
          >
            <Button type="link" size="small" danger>
              Xoá
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

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
  }, [search, status, page, pageSize, reloadKey]);

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

          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Thêm tuyến
          </Button>
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

      <RouteFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
};

export default RouteListPage;
