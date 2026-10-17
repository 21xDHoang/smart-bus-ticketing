import { useCallback, useEffect, useState } from 'react';
import { Button, Popconfirm, Select, Space, Table, Typography, message } from 'antd';
import { PlusOutlined, ReloadOutlined, ThunderboltOutlined } from '@ant-design/icons';
import type { TableProps } from 'antd';
import fareApi from '../api/fareApi';
import { PASSENGER_TYPE_META, PASSENGER_TYPE_OPTIONS } from '../api/fareApi';
import type { Fare, FarePayload, PassengerType, RouteOption } from '../api/fareApi';
import FareFormModal from '../components/FareFormModal';
import FareTemplateEditor from '../components/FareTemplateEditor';
import ApplyFareTemplateModal from '../components/ApplyFareTemplateModal';
import { loadFareTemplate } from '../api/fareTemplateApi';
import { applyDiscount, routeFare } from '../utils/fareTemplateCalc';
import { EmptyState, PageCard, PageHeader, StatusTag } from '../components/ui';
import { formatDateTime, formatVnd } from '../components/ui/format';

// Khoá giả cho lựa chọn "Bảng giá mẫu" trong ô chọn tuyến — không phải id tuyến thật, chỉ đánh
// dấu để màn hình hiển thị bảng giá mẫu theo km thay vì bảng giá của một tuyến cụ thể.
const FARE_TEMPLATE_ROUTE_ID = '__fare-template__';

export default function FareConfigPage() {
  const [routes, setRoutes] = useState<RouteOption[]>([]);
  // Mặc định mở ở "Bảng giá mẫu" — lựa chọn đầu tiên trong ô chọn tuyến.
  const [selectedRouteId, setSelectedRouteId] = useState<string>(FARE_TEMPLATE_ROUTE_ID);
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  const [fares, setFares] = useState<Fare[]>([]);
  const [loadingFares, setLoadingFares] = useState(false);

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<Fare | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const [applyModalOpen, setApplyModalOpen] = useState(false);
  const [applying, setApplying] = useState(false);

  const isTemplateSelected = selectedRouteId === FARE_TEMPLATE_ROUTE_ID;
  const selectedRoute = routes.find((route) => route.id === selectedRouteId);
  const selectedRouteLabel = selectedRoute ? `${selectedRoute.code} — ${selectedRoute.name}` : null;

  const loadRoutes = useCallback(async () => {
    try {
      setRoutes(await fareApi.listRoutes());
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

  // Tải bảng giá mỗi khi đổi tuyến. Đang chọn "Bảng giá mẫu" thì không gọi API — màn đó là form
  // cấu hình quy ước, không phải bảng giá của một tuyến.
  const loadFares = useCallback(async () => {
    if (!selectedRouteId || isTemplateSelected) return;
    setLoadingFares(true);
    try {
      setFares(await fareApi.list(selectedRouteId));
    } catch (error) {
      message.error((error as Error).message || 'Không tải được bảng giá vé.');
      setFares([]);
    } finally {
      setLoadingFares(false);
    }
  }, [selectedRouteId, isTemplateSelected]);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadFares();
  }, [loadFares]);

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
    if (!selectedRouteId || isTemplateSelected) return;
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
    if (!selectedRouteId || isTemplateSelected) return;
    try {
      await fareApi.remove(selectedRouteId, id);
      message.success('Đã xoá giá vé.');
      await loadFares();
    } catch (error) {
      message.error((error as Error).message || 'Xoá thất bại.');
    }
  };

  // Áp bảng giá mẫu theo km vào tuyến đang chọn: giá từng đối tượng = giá/km sau ưu đãi × số km
  // tuyến (làm tròn về đồng). Đối tượng đã có giá thì sửa, chưa có thì thêm.
  const applyFareTemplate = async () => {
    if (!selectedRoute || isTemplateSelected) return;
    const template = loadFareTemplate();
    setApplying(true);
    try {
      for (const option of PASSENGER_TYPE_OPTIONS) {
        const type = option.value;
        const pricePerKm =
          type === 'Standard'
            ? template.adultPricePerKm
            : applyDiscount(template.adultPricePerKm, template.discounts[type]);
        const price = routeFare(pricePerKm, selectedRoute.distanceKm);
        const existing = fares.find((fare) => fare.passengerType === type);
        if (existing) {
          await fareApi.update(selectedRoute.id, existing.id, { price });
        } else {
          await fareApi.create(selectedRoute.id, { passengerType: type, price });
        }
      }
      message.success('Đã áp bảng giá mẫu cho tuyến.');
      setApplyModalOpen(false);
      await loadFares();
    } catch (error) {
      message.error((error as Error).message || 'Áp bảng giá mẫu thất bại.');
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
        subtitle="Đặt giá vé cho từng đối tượng hành khách trên mỗi tuyến, hoặc cấu hình bảng giá mẫu theo km."
        extra={
          <Space wrap>
            <Select
              placeholder="Chọn tuyến"
              style={{ minWidth: 280 }}
              value={selectedRouteId}
              loading={loadingRoutes}
              onChange={(value) => setSelectedRouteId(value)}
              options={[
                { value: FARE_TEMPLATE_ROUTE_ID, label: 'Bảng giá mẫu' },
                ...routes.map((route) => ({
                  value: route.id,
                  label: `${route.code} — ${route.name}`,
                })),
              ]}
            />
            {!isTemplateSelected && (
              <>
                <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
                  Làm mới
                </Button>
                <Button
                  icon={<ThunderboltOutlined />}
                  onClick={() => setApplyModalOpen(true)}
                  disabled={!selectedRoute}
                >
                  Áp dụng bảng giá mẫu
                </Button>
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={openCreate}
                  disabled={allConfigured}
                >
                  Thêm giá
                </Button>
              </>
            )}
          </Space>
        }
      />

      <PageCard>
        {isTemplateSelected ? (
          <FareTemplateEditor />
        ) : !selectedRoute && !loadingRoutes ? (
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

      <ApplyFareTemplateModal
        open={applyModalOpen}
        routeLabel={selectedRouteLabel ?? ''}
        distanceKm={selectedRoute?.distanceKm ?? 0}
        existingCount={fares.length}
        applying={applying}
        onCancel={() => setApplyModalOpen(false)}
        onApply={() => void applyFareTemplate()}
      />
    </div>
  );
}
