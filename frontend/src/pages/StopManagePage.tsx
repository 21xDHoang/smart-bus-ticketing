import { useCallback, useEffect, useMemo, useState } from 'react';
import { Button, Input, Popconfirm, Space, Table, Tag, Typography, message } from 'antd';
import { EnvironmentOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import stopApi from '../api/stopApi';
import type { Stop, StopPayload } from '../api/stopApi';
import StopFormModal from '../components/StopFormModal';

const { Title } = Typography;

export default function StopManagePage() {
  const [stops, setStops] = useState<Stop[]>([]);
  // Bắt đầu ở trạng thái "đang tải" — lần load đầu tiên chạy ngay khi mount nên
  // không cần setLoading(true) đồng bộ trong effect (tránh render thừa).
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState('');

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Stop | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const loadStops = useCallback(async () => {
    try {
      setStops(await stopApi.list());
    } catch (error) {
      message.error((error as Error).message || 'Không tải được danh sách trạm dừng.');
    } finally {
      setLoading(false);
    }
  }, []);

  // Lần tải đầu tiên khi mở trang.
  useEffect(() => {
    void loadStops();
  }, [loadStops]);

  // Nút "Làm mới" do người dùng bấm — bật spinner rồi tải lại.
  const handleRefresh = () => {
    setLoading(true);
    void loadStops();
  };

  // Lọc theo tên/địa chỉ ngay trên client (backend chưa có query param tìm kiếm).
  const filtered = useMemo(() => {
    const kw = keyword.trim().toLowerCase();
    if (!kw) return stops;
    return stops.filter(
      (stop) =>
        stop.name.toLowerCase().includes(kw) || stop.address.toLowerCase().includes(kw),
    );
  }, [stops, keyword]);

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (stop: Stop) => {
    setEditing(stop);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: StopPayload, id?: string) => {
    setSubmitting(true);
    try {
      if (id) {
        await stopApi.update(id, payload);
        message.success('Đã cập nhật trạm dừng.');
      } else {
        await stopApi.create(payload);
        message.success('Đã thêm trạm dừng.');
      }
      setModalOpen(false);
      await loadStops();
    } catch (error) {
      message.error((error as Error).message || 'Thao tác thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await stopApi.remove(id);
      message.success('Đã xoá trạm dừng.');
      await loadStops();
    } catch (error) {
      message.error((error as Error).message || 'Xoá thất bại.');
    }
  };

  const columns: TableProps<Stop>['columns'] = [
    {
      title: 'Tên trạm dừng',
      dataIndex: 'name',
      key: 'name',
      render: (name: string) => (
        <Space>
          <EnvironmentOutlined style={{ color: '#4361ee' }} />
          <span style={{ fontWeight: 600 }}>{name}</span>
        </Space>
      ),
    },
    {
      title: 'Địa chỉ',
      dataIndex: 'address',
      key: 'address',
      ellipsis: true,
    },
    {
      title: 'Toạ độ',
      key: 'coords',
      width: 220,
      render: (_, stop) => (
        <Space size={6} wrap>
          <Tag color="blue">lat {stop.latitude.toFixed(5)}</Tag>
          <Tag color="geekblue">lng {stop.longitude.toFixed(5)}</Tag>
        </Space>
      ),
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 140,
      render: (_, stop) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(stop)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xoá trạm dừng?"
            description={`Trạm “${stop.name}” sẽ bị xoá vĩnh viễn.`}
            okText="Xoá"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(stop.id)}
          >
            <Button type="link" size="small" danger>
              Xoá
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 12,
          marginBottom: 20,
        }}
      >
        <div>
          <Title level={4} style={{ margin: 0 }}>
            Quản lý Trạm dừng
          </Title>
          <span style={{ color: '#94a3b8' }}>
            Thêm, sửa trạm dừng và chọn toạ độ trên bản đồ.
          </span>
        </div>

        <Space wrap>
          <Input.Search
            placeholder="Tìm theo tên / địa chỉ…"
            allowClear
            style={{ width: 260 }}
            onChange={(event) => setKeyword(event.target.value)}
          />
          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            Thêm trạm
          </Button>
        </Space>
      </div>

      <Table<Stop>
        rowKey="id"
        columns={columns}
        dataSource={filtered}
        loading={loading}
        pagination={{ pageSize: 6, showSizeChanger: true }}
      />

      <StopFormModal
        open={modalOpen}
        editing={editing}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
}
