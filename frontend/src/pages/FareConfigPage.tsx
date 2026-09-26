import { useCallback, useEffect, useState } from 'react';
import { Button, Empty, Popconfirm, Select, Space, Table, Tag, Typography, message } from 'antd';
import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import fareApi from '../api/fareApi';
import { PASSENGER_TYPE_META, PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { Fare, FarePayload, PassengerType, RouteOption } from '../api/fareApi';
import FareFormModal from '../components/FareFormModal';

const { Title } = Typography;

export default function FareConfigPage() {
  const [routes, setRoutes] = useState<RouteOption[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string>();
  // Bắt đầu ở trạng thái "đang tải" cho danh sách tuyến — lần load đầu chạy ngay khi mount.
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [fares, setFares] = useState<Fare[]>([]);
  const [loadingFares, setLoadingFares] = useState(false);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Fare | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Tải danh sách tuyến cho ô chọn, tự chọn tuyến đầu tiên khi mở trang.
  const loadRoutes = useCallback(async () => {
    try {
      const list = await fareApi.listRoutes();
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

  // Tải bảng giá mỗi khi đổi tuyến.
  const loadFares = useCallback(async () => {
    if (!selectedRouteId) return;
    setLoadingFares(true);
    try {
      setFares(await fareApi.list(selectedRouteId));
    } catch (error) {
      message.error((error as Error).message || 'Không tải được bảng giá vé.');
      setFares([]);
    } finally {
      setLoadingFares(false);
    }
  }, [selectedRouteId]);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadFares();
  }, [loadFares]);

  // Nút "Làm mới" do người dùng bấm.
  const handleRefresh = () => void loadFares();

  // Các đối tượng tuyến đang chọn đã có giá — dùng để chặn chọn trùng khi thêm mới.
  const existingTypes = fares.map((fare) => fare.passengerType);
  const allConfigured = existingTypes.length === PASSENGER_TYPE_OPTIONS.length;

  const openCreate = () => {
    setEditing(null);
    setModalOpen(true);
  };

  const openEdit = (fare: Fare) => {
    setEditing(fare);
    setModalOpen(true);
  };

  const handleSubmit = async (payload: FarePayload, id?: string) => {
    if (!selectedRouteId) return;
    setSubmitting(true);
    try {
      if (id) {
        await fareApi.update(selectedRouteId, id, payload);
        message.success('Đã cập nhật giá vé.');
      } else {
        await fareApi.create(selectedRouteId, payload);
        message.success('Đã thêm giá vé.');
      }
      setModalOpen(false);
      await loadFares();
    } catch (error) {
      message.error((error as Error).message || 'Thao tác thất bại.');
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!selectedRouteId) return;
    try {
      await fareApi.remove(selectedRouteId, id);
      message.success('Đã xoá giá vé.');
      await loadFares();
    } catch (error) {
      message.error((error as Error).message || 'Xoá thất bại.');
    }
  };

  const columns: TableProps<Fare>['columns'] = [
    {
      title: 'Đối tượng áp dụng',
      dataIndex: 'passengerType',
      key: 'passengerType',
      render: (type: PassengerType) => {
        const meta = PASSENGER_TYPE_META[type];
        return (
          <Space>
            <Tag color={meta.color}>{meta.label}</Tag>
            {meta.isDiscount && <Tag color="gold">Ưu đãi</Tag>}
          </Space>
        );
      },
    },
    {
      title: 'Giá vé',
      dataIndex: 'price',
      key: 'price',
      align: 'right',
      render: (price: number) => (
        <span style={{ fontWeight: 600 }}>{price.toLocaleString('vi-VN')} ₫</span>
      ),
    },
    {
      title: 'Cập nhật lần cuối',
      key: 'updatedAt',
      render: (_, fare) => {
        const at = fare.updatedAt ?? fare.createdAt;
        return (
          <Typography.Text type="secondary">
            {dayjs(at).format('DD/MM/YYYY HH:mm')}
          </Typography.Text>
        );
      },
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 140,
      render: (_, fare) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(fare)}>
            Sửa
          </Button>
          <Popconfirm
            title="Xoá giá vé?"
            description={`Giá cho đối tượng “${PASSENGER_TYPE_META[fare.passengerType].label}” sẽ bị xoá.`}
            okText="Xoá"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => handleDelete(fare.id)}
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
            Cấu hình bảng giá vé
          </Title>
          <span style={{ color: '#94a3b8' }}>
            Đặt giá vé cho từng đối tượng hành khách trên mỗi tuyến.
          </span>
        </div>

        <Space wrap>
          <Select
            placeholder="Chọn tuyến"
            style={{ minWidth: 280 }}
            value={selectedRouteId}
            loading={loadingRoutes}
            onChange={(value) => setSelectedRouteId(value)}
            options={routes.map((route) => ({
              value: route.id,
              label: `${route.code} — ${route.name}`,
            }))}
          />
          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={openCreate}
            disabled={!selectedRouteId || allConfigured}
          >
            Thêm giá
          </Button>
        </Space>
      </div>

      {!selectedRouteId && !loadingRoutes ? (
        <Empty description="Chưa có tuyến nào để cấu hình giá vé." />
      ) : (
        <Table<Fare>
          rowKey="id"
          columns={columns}
          dataSource={fares}
          loading={loadingFares}
          pagination={false}
          locale={{ emptyText: 'Tuyến này chưa cấu hình giá vé nào' }}
        />
      )}

      <FareFormModal
        open={modalOpen}
        editing={editing}
        existingTypes={existingTypes}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />
    </div>
  );
}
