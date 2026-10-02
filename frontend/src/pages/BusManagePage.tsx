import { useEffect, useState } from 'react';
import { Button, Card, Input, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { PlusOutlined } from '@ant-design/icons';
import { fetchBuses, BUS_STATUS_META } from '../api/busApi';
import type { Bus, BusStatus } from '../api/busApi';
import busCrudApi, { BUS_STATUS_OPTIONS } from '../api/busCrudApi';
import type { BusPayload, UpdateBusPayload } from '../api/busCrudApi';
import type { AppError } from '../api/axiosClient';
import BusFormModal from '../components/BusFormModal';

// Màn hình quản lý đội xe (story 14): bảng + tìm kiếm + lọc trạng thái + phân trang.
// Phần Thêm/Sửa xe + xác nhận ngừng khai thác (nút, modal, popconfirm) là task "Màn hình
// quản lý đội xe" của Dương Thị Hạnh, gắn vào đây dùng chung một trang.
const BASE_COLUMNS: TableProps<Bus>['columns'] = [
  {
    title: 'Biển số',
    dataIndex: 'licensePlate',
    key: 'licensePlate',
    width: 130,
    render: (licensePlate: string) => <Tag color="blue">{licensePlate}</Tag>,
  },
  {
    title: 'Loại xe',
    dataIndex: 'busType',
    key: 'busType',
    width: 180,
  },
  {
    title: 'Sức chứa',
    dataIndex: 'capacity',
    key: 'capacity',
    width: 110,
    render: (capacity: number) => `${capacity} chỗ`,
  },
  {
    title: 'Trạng thái',
    dataIndex: 'status',
    key: 'status',
    width: 170,
    render: (status: BusStatus) => {
      // Backend chỉ trả 'Active' | 'Maintenance' | 'Inactive'. Lỡ có giá trị lạ thì hiện
      // nguyên văn, không để màn hình vỡ vì tra meta không thấy.
      const meta = BUS_STATUS_META[status];
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

const BusManagePage = () => {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<BusStatus>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<Bus[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/xoá thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Bus | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (bus: Bus) => {
    setEditing(bus);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: BusPayload | UpdateBusPayload, id?: string) => {
    setSubmitting(true);
    try {
      if (id) {
        await busCrudApi.update(id, payload as UpdateBusPayload);
        message.success('Đã cập nhật xe buýt.');
      } else {
        await busCrudApi.create(payload);
        message.success('Đã thêm xe buýt.');
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
      await busCrudApi.remove(id);
      message.success('Đã ngừng khai thác xe buýt.');
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
  const columns: TableProps<Bus>['columns'] = [
    ...BASE_COLUMNS,
    {
      title: 'Hành động',
      key: 'actions',
      width: 140,
      render: (_, bus) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(bus)}>
            Sửa
          </Button>
          <Popconfirm
            title="Ngừng khai thác xe?"
            description={`Xe “${bus.licensePlate}” sẽ chuyển sang trạng thái ngừng khai thác.`}
            okText="Ngừng"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(bus.id)}
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
    fetchBuses({ page, pageSize, search: search || undefined, status })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách đội xe.');
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
          Quản lý đội xe
        </Typography.Title>
        <Typography.Text type="secondary">
          Tra cứu xe theo biển số, loại xe và trạng thái khai thác — bảo dưỡng.
        </Typography.Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Input.Search
            allowClear
            placeholder="Tìm theo biển số hoặc loại xe"
            style={{ width: 280 }}
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

          <Select<BusStatus>
            allowClear
            placeholder="Trạng thái"
            style={{ width: 200 }}
            options={BUS_STATUS_OPTIONS}
            onChange={(value) => {
              setStatus(value ?? undefined);
              setPage(1);
            }}
          />

          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Thêm xe
          </Button>
        </Space>

        <Table<Bus>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 720 }}
          locale={{ emptyText: 'Không tìm thấy xe buýt nào' }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showQuickJumper: true,
            pageSizeOptions: [10, 20, 50],
            showTotal: (t, range) => `Hiển thị ${range[0]}–${range[1]} trên ${t} xe`,
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          }}
        />
      </Card>

      <BusFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
};

export default BusManagePage;
