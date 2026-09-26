import { useEffect, useState } from 'react';
import { Avatar, Card, Space, Table, Tag, Typography } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import { PhoneOutlined, UserOutlined } from '@ant-design/icons';
import { useAuth } from '../contexts';
import { fetchMyLoginActivity, getAuditActionMeta } from '../api/auditLogApi';
import type { AuditLog } from '../api/auditLogApi';
import { getRoleMeta } from '../api/adminUserApi';

const { Title, Text } = Typography;

// Cột của bảng "Nhật ký đăng nhập gần nhất" — chỉ hiển thị, không phân trang (vài dòng).
const columns: TableProps<AuditLog>['columns'] = [
  {
    title: 'Thời gian',
    dataIndex: 'createdAt',
    key: 'createdAt',
    width: 190,
    render: (createdAt: string) => dayjs(createdAt).format('DD/MM/YYYY HH:mm:ss'),
  },
  {
    title: 'Hành động',
    dataIndex: 'action',
    key: 'action',
    width: 170,
    render: (action: string) => {
      const meta = getAuditActionMeta(action);
      return <Tag color={meta.color}>{meta.label}</Tag>;
    },
  },
  {
    title: 'Địa chỉ IP',
    dataIndex: 'ipAddress',
    key: 'ipAddress',
    render: (ip: string | null) =>
      ip ? <Text code>{ip}</Text> : <Text type="secondary">Không xác định</Text>,
  },
];

// Trang cá nhân: thông tin tài khoản + nhật ký đăng nhập gần nhất (story 23).
// Ai đã đăng nhập đều vào được — không giới hạn vai trò như các màn hình quản trị.
const ProfilePage = () => {
  const { user } = useAuth();
  const [activity, setActivity] = useState<AuditLog[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    // user.id là claim `sub` trong JWT. Nếu token lạ thiếu claim thì không có gì để tra —
    // bỏ qua, danh sách mặc định đã rỗng.
    if (!user?.id) return;

    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự (dữ liệu giả trong lúc chờ
    // backend), không phải "state dẫn xuất" nên tắt cảnh báo cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    fetchMyLoginActivity(user.id)
      .then((logs) => {
        if (!cancelled) setActivity(logs);
      })
      .catch(() => {
        // Hiện là dữ liệu giả nên khó xảy ra lỗi; nếu có thì giữ danh sách rỗng.
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [user?.id]);

  const roleMeta = getRoleMeta(user?.role ?? '');

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Trang cá nhân
        </Title>
        <Text type="secondary">Thông tin tài khoản và nhật ký đăng nhập gần nhất của bạn.</Text>
      </div>

      <Space direction="vertical" size={16} style={{ width: '100%' }}>
        <Card
          variant="borderless"
          style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
        >
          <Space size={16} align="center">
            <Avatar size={56} style={{ backgroundColor: roleMeta.bg }}>
              {user?.fullName ? user.fullName.trim().charAt(0).toUpperCase() : <UserOutlined />}
            </Avatar>
            <div>
              <div style={{ fontSize: 18, fontWeight: 700 }}>{user?.fullName || 'Người dùng'}</div>
              <Space size={8} wrap style={{ marginTop: 4 }}>
                <Tag color={roleMeta.color}>{roleMeta.label}</Tag>
                {user?.phoneNumber && (
                  <Text type="secondary">
                    <PhoneOutlined /> {user.phoneNumber}
                  </Text>
                )}
              </Space>
            </div>
          </Space>
        </Card>

        <Card
          variant="borderless"
          title="Nhật ký đăng nhập gần nhất"
          style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
        >
          <Table<AuditLog>
            rowKey="id"
            columns={columns}
            dataSource={activity}
            loading={loading}
            pagination={false}
            locale={{ emptyText: 'Chưa có hoạt động đăng nhập nào' }}
          />
        </Card>
      </Space>
    </div>
  );
};

export default ProfilePage;
