import { useEffect, useState } from 'react';
import { Avatar, Button, Input, Popconfirm, Select, Space, Table, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import {
  createAdminUser,
  deleteAdminUser,
  fetchAdminUsers,
  getRoleMeta,
  isDriver,
  primaryRoleOf,
  resetAdminUserPassword,
  ROLE_OPTIONS,
  setAdminUserRoles,
  setAdminUserStatus,
  updateAdminUser,
} from '../api/adminUserApi';
import type { AdminUser, RoleCode } from '../api/adminUserApi';
import type { AppError } from '../api/axiosClient';
import AdminUserFormModal from '../components/AdminUserFormModal';
import type { AdminUserFormSubmission } from '../components/AdminUserFormModal';
import DriverAssignmentInfo from '../components/DriverAssignmentInfo';
import { FilterBar, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDate } from '../components/ui/format';
import { tablePagination } from '../components/ui/table';
import { useAuth } from '../contexts';

/** Trạng thái tài khoản dùng cho bộ lọc — ánh xạ sang AdminUser.isActive. */
type StatusFilter = 'active' | 'locked';

const STATUS_OPTIONS: { value: StatusFilter; label: string }[] = [
  { value: 'active', label: 'Đang hoạt động' },
  { value: 'locked', label: 'Đã khóa' },
];

/** Hai mảng vai trò bằng nhau (so theo tập, không phụ thuộc thứ tự người dùng bấm chọn). */
function sameRoles(a: RoleCode[], b: RoleCode[]): boolean {
  if (a.length !== b.length) return false;
  const setA = new Set(a);
  return b.every((role) => setA.has(role));
}

/** Toàn bộ vai trò của tài khoản, xếp theo thứ tự hiển thị của ROLE_OPTIONS. */
function rolesOf(user: AdminUser): RoleCode[] {
  const roles = user.roles?.length ? user.roles : [user.role];
  return ROLE_OPTIONS.map((option) => option.value).filter((role) => roles.includes(role));
}

const AdminUserListPage = () => {
  const { user: currentUser } = useAuth();

  const [search, setSearch] = useState('');
  const [role, setRole] = useState<RoleCode>();
  const [status, setStatus] = useState<StatusFilter>();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const [data, setData] = useState<AdminUser[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  // Tăng giá trị để tải lại danh sách sau khi thêm/sửa/xóa thành công.
  const [reloadKey, setReloadKey] = useState(0);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<AdminUser | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (user: AdminUser) => {
    setEditing(user);
    setModalOpen(true);
  };

  const handleSubmit = async (
    submission: AdminUserFormSubmission,
    editingUser: AdminUser | null,
  ) => {
    setSubmitting(true);
    try {
      if (editingUser) {
        // Ba endpoint riêng của backend: hồ sơ, vai trò, mật khẩu. Chỉ gọi cái thực sự đổi
        // — đỡ một vòng mạng và tránh vô tình thu hồi phiên khi mật khẩu không đổi.
        await updateAdminUser(editingUser.id, {
          fullName: submission.fullName,
          phoneNumber: submission.phoneNumber,
          email: submission.email,
        });

        if (!sameRoles(rolesOf(editingUser), submission.roles)) {
          await setAdminUserRoles(editingUser.id, submission.roles);
        }

        if (submission.password) {
          await resetAdminUserPassword(editingUser.id, submission.password);
        }

        if (editingUser.isActive !== submission.isActive) {
          await setAdminUserStatus(editingUser.id, submission.isActive);
        }

        message.success('Đã cập nhật tài khoản.');
      } else {
        // POST chỉ nhận MỘT vai trò chính; chọn nhiều thì tạo xong gán bổ sung bằng PUT /roles.
        const primaryRole = primaryRoleOf(submission.roles);
        if (!primaryRole || !submission.password) {
          throw new Error('Dữ liệu tài khoản chưa đầy đủ.');
        }

        const created = await createAdminUser({
          fullName: submission.fullName,
          phoneNumber: submission.phoneNumber,
          email: submission.email,
          password: submission.password,
          roleCode: primaryRole,
        });

        if (submission.roles.length > 1) {
          try {
            await setAdminUserRoles(created.id, submission.roles);
          } catch {
            // Tài khoản đã tạo xong — không ném lỗi để tránh người dùng bấm lại và tạo
            // trùng SĐT. Vai trò thiếu gán được ngay ở lần sửa kế tiếp.
            message.warning(
              'Đã tạo tài khoản nhưng chưa gán đủ vai trò. Hãy bấm Sửa để gán lại.',
            );
          }
        }

        message.success('Đã thêm tài khoản.');
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
      await deleteAdminUser(id);
      message.success('Đã xóa tài khoản (khóa đăng nhập, dữ liệu vẫn được giữ).');
      // Xoá dòng cuối của trang cuối thì lùi về trang trước — tránh đứng ở trang rỗng.
      if (data.length === 1 && page > 1) {
        setPage(page - 1);
      } else {
        setReloadKey((key) => key + 1);
      }
    } catch (error) {
      message.error((error as AppError).customMessage || 'Thao tác thất bại.');
    }
  };

  const handleUnlock = async (id: string) => {
    try {
      await setAdminUserStatus(id, true);
      message.success('Đã mở khóa tài khoản.');
      setReloadKey((key) => key + 1);
    } catch (error) {
      message.error((error as AppError).customMessage || 'Thao tác thất bại.');
    }
  };

  // Cột "Hành động" cần state/handler của trang nên ghép ở đây, không để ở module.
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
      key: 'role',
      width: 190,
      render: (_, record: AdminUser) => (
        <Space size={[4, 4]} wrap>
          {rolesOf(record).map((code) => {
            const meta = getRoleMeta(code);
            return (
              <StatusTag key={code} color={meta.color}>
                {meta.label}
              </StatusTag>
            );
          })}
        </Space>
      ),
    },
    {
      title: 'Phân công',
      key: 'assignment',
      width: 300,
      render: (_, record: AdminUser) =>
        isDriver(record) ? (
          <DriverAssignmentInfo driverId={record.id} compact />
        ) : (
          <Typography.Text type="secondary">—</Typography.Text>
        ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'isActive',
      key: 'isActive',
      width: 130,
      render: (isActive: boolean) =>
        isActive ? (
          <StatusTag color="success">Hoạt động</StatusTag>
        ) : (
          <StatusTag color="error">Bị khóa</StatusTag>
        ),
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 130,
      render: (createdAt: string) => formatDate(createdAt),
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 170,
      render: (_, record: AdminUser) => {
        const isSelf = record.id === currentUser?.id;

        return (
          <Space size={0}>
            <Button type="link" size="small" onClick={() => openEdit(record)}>
              Sửa
            </Button>

            {record.isActive ? (
              isSelf ? (
                // Backend chặn tự khóa (409); ẩn nút để người dùng không đi vào ngõ cụt.
                // Nút "xóa" của chính mình cũng chính là khóa — cùng lý do.
                <Button type="link" size="small" disabled>
                  Xóa
                </Button>
              ) : (
                <Popconfirm
                  title="Xóa tài khoản?"
                  description={`Tài khoản “${record.fullName}” sẽ bị khóa (xoá mềm). Dữ liệu vé/chuyến liên quan vẫn giữ nguyên.`}
                  okText="Xóa"
                  cancelText="Huỷ"
                  okButtonProps={{ danger: true }}
                  onConfirm={() => handleDelete(record.id)}
                >
                  <Button type="link" size="small" danger>
                    Xóa
                  </Button>
                </Popconfirm>
              )
            ) : (
              <Popconfirm
                title="Mở khóa tài khoản?"
                description={`Tài khoản “${record.fullName}” sẽ đăng nhập lại được.`}
                okText="Mở khóa"
                cancelText="Huỷ"
                onConfirm={() => handleUnlock(record.id)}
              >
                <Button type="link" size="small">
                  Mở khóa
                </Button>
              </Popconfirm>
            )}
          </Space>
        );
      },
    },
  ];

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
  }, [search, role, status, page, pageSize, reloadKey]);

  return (
    <div>
      <PageHeader
        title="Danh sách người dùng"
        subtitle="Thêm, sửa, xóa tài khoản Admin, Quản lý, Tài xế và Hành khách trong hệ thống."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
                Thêm tài khoản
              </Button>
            }
          >
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
          </FilterBar>
        }
      >
        <Table<AdminUser>
          rowKey="id"
          columns={columns}
          dataSource={data}
          loading={loading}
          scroll={{ x: 1180 }}
          locale={{ emptyText: 'Không tìm thấy người dùng nào' }}
          pagination={tablePagination({
            page,
            pageSize,
            total,
            unitLabel: 'người dùng',
            onChange: (nextPage, nextPageSize) => {
              // Đổi cỡ trang thì quay về trang 1 để tránh đứng ở trang không còn tồn tại.
              setPage(nextPageSize !== pageSize ? 1 : nextPage);
              setPageSize(nextPageSize);
            },
          })}
        />
      </PageCard>

      <AdminUserFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
};

export default AdminUserListPage;
