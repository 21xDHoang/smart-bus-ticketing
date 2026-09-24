import { useEffect, useState } from 'react';
import { Avatar, Card, Input, Select, Space, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { fetchAdminUsers, getRoleMeta, ROLE_OPTIONS } from '../api/adminUserApi';
import type { AdminUser, RoleCode } from '../api/adminUserApi';
import type { AppError } from '../api/axiosClient';

/** Trạng thái tài khoản dùng cho bộ lọc — ánh xạ sang AdminUser.isActive. */
type StatusFilter = 'active' | 'locked';

const STATUS_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: 'active', label: 'Đang hoạt động' },
  { value: 'locked', label: 'Đã khóa' },
];

const columns: TableProps<AdminUser>['columns'] = [
  {
    title: 'Người dùng',
    key: 'user',
    render: (_, record: AdminUser) => {
      const meta = getRoleMeta(record.role);
      return (
        <Space>
          <Avatar style={{ backgroundColor: meta.bg }}>
            {record.fullName.trim().charAt(0).toUpperCase()}
          </Avatar>
          <div>
            <div style={{ fontWeight: 600 }}>{record.fullName}</div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {record.email || 'Chưa có email'}
            </Typography.Text>
          </div>
        </Space>
      );
    },
  },
  {
    title: 'Số điện thoại',
    dataIndex: 'phoneNumber',
    key: 'phoneNumber',
    width: 150,
  },
  {
    title: 'Vai trò',
    dataIndex: 'role',
    key: 'role',
    width: 140,
    render: (role: RoleCode) => {
      const meta = getRoleMeta(role);
      return <Tag color={meta.color}>{meta.label}</Tag>;
    },
  },
  {
    title: 'Trạng thái',
    dataIndex: 'isActive',
    key: 'isActive',
    width: 130,
    render: (isActive: boolean) =>
      isActive ? <Tag color="success">Hoạt động</Tag> : <Tag color="error">Bị khóa</Tag>,
  },
  {
    title: 'Ngày tạo',
    dataIndex: 'createdAt',
    key: 'createdAt',
    width: 130,
    render: (createdAt: string) => dayjs(createdAt).format('DD/MM/YYYY'),
  },
];

const AdminUserListPage = () => {
  const [search, setSearch] = useState('');
  const [role, setRole] = useState<RoleCode>();
  const [status, setStatus] = useState<StatusFilter>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<AdminUser[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    let cancelled = false;
    const isActive = status === 'active' ? true : status === 'locked' ? false : undefined;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchAdminUsers({ page, pageSize, search: search || undefined, role, isActive })
      .then((result) => {
        if (cancelled) return;
        setData(result.items);
        setTotal(result.total);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách người dùng.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [search, role, status, page, pageSize]);

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Typography.Title level={4} style={{ margin: 0 }}>
          Danh sách người dùng
        </Typography.Title>
        <Typography.Text type="secondary">
          Quản lý tài khoản Admin, Quản lý, Tài xế và Hành khách trong hệ thống.
        </Typography.Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Input.Search
            allowClear
            placeholder="Tìm theo tên, SĐT hoặc email"
            style={{ width: 300 }}
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

          <Select<RoleCode>
            allowClear
            placeholder="Vai trò"
            style={{ width: 180 }}
            options={ROLE_OPTIONS}
            onChange={(value) => {
              setRole(value ?? undefined);
              setPage(1);
            }}
          />

          <Select<StatusFilter>
            allowClear
            placeholder="Trạng thái"
            style={{ width: 180 }}
            options={STATUS_OPTIONS}
            onChange={(value) => {
              setStatus(value ?? undefined);
              setPage(1);
            }}
          />
        </Space>

        <Table<AdminUser>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 760 }}
          locale={{ emptyText: 'Không tìm thấy người dùng nào' }}
          pagination={{
            current: page,
            pageSize,
            total,
            showSizeChanger: true,
            showQuickJumper: true,
            pageSizeOptions: [10, 20, 50],
            showTotal: (t, range) => `Hiển thị ${range[0]}–${range[1]} trên ${t} người dùng`,
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

export default AdminUserListPage;
