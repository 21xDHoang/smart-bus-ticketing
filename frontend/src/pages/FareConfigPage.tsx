import { useCallback, useEffect, useState } from 'react';
import { Button, Popconfirm, Select, Space, Table, Typography, message } from 'antd';
import { PlusOutlined, ReloadOutlined, TableOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import fareApi from '../api/fareApi';
import { PASSENGER_TYPE_META, PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { Fare, FarePayload, PassengerType, RouteOption } from '../api/fareApi';
import FareFormModal from '../components/FareFormModal';
import FareTableModal from '../components/FareTableModal';
import type { FareTable } from '../api/fareTableApi';
import { EmptyState, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

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

  const [tableModalOpen, setTableModalOpen] = useState(false);
  const [applying, setApplying] = useState(false);

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

  // Nhãn tuyến đang chọn để hiển thị trong modal bảng giá mẫu.
  const selectedRoute = routes.find((route) => route.id === selectedRouteId);
  const selectedRouteLabel = selectedRoute ? `${selectedRoute.code} — ${selectedRoute.name}` : null;

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

  // Áp bảng giá mẫu vào tuyến đang chọn — gọi từng endpoint sẵn có của FaresController:
  // đối tượng đã có giá thì sửa, chưa có thì thêm. Làm xong nạp lại bảng giá của tuyến.
  const applyFareTable = async (table: FareTable) => {
    if (!selectedRouteId) return;
    setApplying(true);
    try {
      for (const option of PASSENGER_TYPE_OPTIONS) {
        const type = option.value;
        const price = table.prices[type];
        const existing = fares.find((fare) => fare.passengerType === type);
        if (existing) {
          await fareApi.update(selectedRouteId, existing.id, { price });
        } else {
          await fareApi.create(selectedRouteId, { passengerType: type, price });
        }
      }
      message.success(`Đã áp bảng giá “${table.name}” cho tuyến.`);
      setTableModalOpen(false);
      await loadFares();
    } catch (error) {
      message.error((error as Error).message || 'Áp bảng giá thất bại.');
    } finally {
      setApplying(false);
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
            <StatusTag color={meta.color}>{meta.label}</StatusTag>
            {meta.isDiscount && <StatusTag color="gold">Ưu đãi</StatusTag>}
          </Space>
        );
      },
    },
    {
      title: 'Giá vé',
      dataIndex: 'price',
      key: 'price',
      align: 'right',
      render: (price: number) => <span style={{ fontWeight: 600 }}>{formatVnd(price)}</span>,
    },
    {
      title: 'Cập nhật lần cuối',
      key: 'updatedAt',
      render: (_, fare) => {
        const at = fare.updatedAt ?? fare.createdAt;
        return (
          <Typography.Text type="secondary">{formatDateTime(at)}</Typography.Text>
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
      <PageHeader
        title="Cấu hình bảng giá vé"
        subtitle="Đặt giá vé cho từng đối tượng hành khách trên mỗi tuyến."
        extra={
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
            <Button icon={<TableOutlined />} onClick={() => setTableModalOpen(true)}>
              Bảng giá mẫu
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
        }
      />

      <PageCard>
        {!selectedRouteId && !loadingRoutes ? (
          <EmptyState description="Chưa có tuyến nào để cấu hình giá vé." />
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
      </PageCard>

      <FareFormModal
        open={modalOpen}
        editing={editing}
        existingTypes={existingTypes}
        submitting={submitting}
        onCancel={() => setModalOpen(false)}
        onSubmit={handleSubmit}
      />

      <FareTableModal
        open={tableModalOpen}
        routeLabel={selectedRouteLabel}
        applying={applying}
        onCancel={() => setTableModalOpen(false)}
        onApply={(table) => void applyFareTable(table)}
      />
    </div>
  );
}
