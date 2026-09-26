import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Button,
  Card,
  Empty,
  InputNumber,
  Modal,
  Select,
  Space,
  Spin,
  Typography,
  message,
} from 'antd';
import { PlusOutlined, ReloadOutlined, SaveOutlined } from '@ant-design/icons';
import RouteStopBoard from '../components/RouteStopBoard';
import {
  assignStop,
  fetchRouteStops,
  fetchStopOptions,
  removeRouteStop,
  reorderRouteStops,
} from '../api/routeStopApi';
import type { RouteStop, StopOption } from '../api/routeStopApi';
import { fetchRoutes } from '../api/routeApi';
import type { Route } from '../api/routeApi';
import type { AppError } from '../api/axiosClient';

const { Title, Text } = Typography;

/**
 * Gán trạm vào tuyến và sắp xếp thứ tự trạm (story 12).
 *
 * Trang riêng có ô chọn tuyến, KHÔNG dùng URL `/routes/:id/stops` — theo đúng tiền lệ
 * `FareConfigPage.tsx`. Nhờ vậy không phải sửa `RouteListPage.tsx` (file của Dương Thị
 * Hạnh, đang có task "Form Thêm/Sửa tuyến đường").
 *
 * Thứ tự trạm không phải chi tiết phụ: theo quy ước A8.6, chiều đi của tuyến được xác
 * định bằng `stopOrder` chứ không bằng khoảng cách toạ độ — xe đi và xe về trên cùng
 * tuyến trùng toạ độ. Sai thứ tự trạm là sai nghiệp vụ.
 *
 * Gỡ trạm gọi API ngay (không chờ Lưu), còn đổi THỨ TỰ thì phải bấm "Lưu thứ tự": thao
 * tác sắp xếp cần một lần gửi trọn vẹn cả danh sách, không phải từng dòng một.
 */
const RouteStopsPage = () => {
  const [routes, setRoutes] = useState<Route[]>([]);
  const [selectedRouteId, setSelectedRouteId] = useState<string>();
  const [loadingRoutes, setLoadingRoutes] = useState(true);

  /** Thứ tự đang có trên server — mốc để biết người dùng đã đổi gì chưa. */
  const [savedStops, setSavedStops] = useState<RouteStop[]>([]);

  /** Thứ tự đang hiển thị, đổi ngay khi kéo-thả nhưng chỉ gửi lên khi bấm Lưu. */
  const [boardStops, setBoardStops] = useState<RouteStop[]>([]);
  const [loadingStops, setLoadingStops] = useState(false);
  const [saving, setSaving] = useState(false);

  const [stopOptions, setStopOptions] = useState<StopOption[]>([]);
  const [loadingStopOptions, setLoadingStopOptions] = useState(true);
  const [pendingStopId, setPendingStopId] = useState<string>();
  const [pendingDistanceKm, setPendingDistanceKm] = useState<number>();
  const [assigning, setAssigning] = useState(false);

  // Thứ tự hiển thị khác thứ tự đã lưu hay chưa. So theo id từng vị trí: backend trả
  // `stopOrder` mới sau khi lưu nên không thể so bằng chính trường đó.
  const isDirty = useMemo(
    () =>
      savedStops.length !== boardStops.length ||
      savedStops.some((stop, index) => stop.id !== boardStops[index]?.id),
    [savedStops, boardStops],
  );

  // Tải danh sách tuyến cho ô chọn, tự chọn tuyến đầu tiên khi mở trang.
  const loadRoutes = useCallback(async () => {
    try {
      // 100 là trần `pageSize` backend cho phép (docs/api-contract.md). Nhiều tuyến hơn
      // thì ô chọn sẽ thiếu — chấp nhận ở phạm vi đồ án, chưa cần ô tìm kiếm phân trang.
      const result = await fetchRoutes({ page: 1, pageSize: 100 });
      setRoutes(result.items);
      setSelectedRouteId((current) => current ?? result.items[0]?.id);
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không tải được danh sách tuyến.');
    } finally {
      setLoadingRoutes(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadRoutes();
  }, [loadRoutes]);

  // Tải danh sách trạm dừng có thật để chọn mà gán. Chỉ cần một lần cho cả trang.
  const loadStopOptions = useCallback(async () => {
    try {
      setStopOptions(await fetchStopOptions());
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không tải được danh sách trạm dừng.');
    } finally {
      setLoadingStopOptions(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadStopOptions();
  }, [loadStopOptions]);

  // Tải trạm của tuyến đang chọn. Đổi tuyến là nạp lại từ đầu.
  const loadStops = useCallback(async () => {
    if (!selectedRouteId) {
      setSavedStops([]);
      setBoardStops([]);
      return;
    }

    setLoadingStops(true);
    try {
      const list = await fetchRouteStops(selectedRouteId);
      setSavedStops(list);
      setBoardStops(list);
    } catch (error) {
      message.error(
        (error as AppError).customMessage || 'Không tải được danh sách trạm của tuyến.',
      );
      setSavedStops([]);
      setBoardStops([]);
    } finally {
      setLoadingStops(false);
    }
  }, [selectedRouteId]);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadStops();
  }, [loadStops]);

  // Rời trang bằng cách đóng tab / tải lại khi còn thứ tự chưa lưu thì trình duyệt hỏi lại.
  useEffect(() => {
    if (!isDirty) return;

    const handleBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Trình duyệt đời cũ chỉ hiện hộp thoại khi `returnValue` được gán.
      event.returnValue = '';
    };

    window.addEventListener('beforeunload', handleBeforeUnload);
    return () => window.removeEventListener('beforeunload', handleBeforeUnload);
  }, [isDirty]);

  const handleRefresh = () => {
    void loadRoutes();
    void loadStopOptions();
    void loadStops();
  };

  // Đổi tuyến lúc còn thứ tự chưa lưu thì hỏi trước, vì thao tác đó bỏ hết thay đổi.
  const handleRouteChange = (value: string) => {
    if (!isDirty) {
      setSelectedRouteId(value);
      return;
    }

    Modal.confirm({
      title: 'Bỏ thay đổi thứ tự chưa lưu?',
      content: 'Thứ tự trạm vừa sắp chưa được lưu. Đổi tuyến sẽ bỏ các thay đổi đó.',
      okText: 'Bỏ thay đổi',
      cancelText: 'Ở lại',
      okButtonProps: { danger: true },
      onOk: () => setSelectedRouteId(value),
    });
  };

  const handleSave = async () => {
    if (!selectedRouteId) return;

    setSaving(true);
    try {
      // Cố ý CHỈ gửi `stopId`, bỏ trống `distanceKm`: bỏ trống nghĩa là giữ nguyên khoảng
      // cách đang có, còn gửi 0 là xoá sạch khoảng cách quản lý đã nhập
      // (docs/api-contract.md, mục PUT /routes/{routeId}/stops/order).
      const updated = await reorderRouteStops(
        selectedRouteId,
        boardStops.map((stop) => ({ stopId: stop.stopId })),
      );

      setSavedStops(updated);
      setBoardStops(updated);
      message.success('Đã lưu thứ tự trạm.');
    } catch (error) {
      message.error((error as AppError).customMessage || 'Lưu thứ tự trạm thất bại.');
    } finally {
      setSaving(false);
    }
  };

  const handleAssign = async () => {
    if (!selectedRouteId || !pendingStopId) return;

    setAssigning(true);
    try {
      // POST luôn nối vào CUỐI tuyến. Muốn chèn vào giữa thì gán xong rồi kéo-thả.
      await assignStop(selectedRouteId, {
        stopId: pendingStopId,
        distanceKm: pendingDistanceKm,
      });

      message.success('Đã gán trạm vào cuối tuyến.');
      setPendingStopId(undefined);
      setPendingDistanceKm(undefined);
      await loadStops();
    } catch (error) {
      message.error((error as AppError).customMessage || 'Gán trạm vào tuyến thất bại.');
    } finally {
      setAssigning(false);
    }
  };

  const handleRemove = async (stop: RouteStop) => {
    if (!selectedRouteId) return;

    try {
      // `stop.id` là khoá DÒNG bảng nối — đúng thứ DELETE cần, KHÔNG phải `stop.stopId`.
      await removeRouteStop(selectedRouteId, stop.id);
      message.success('Đã gỡ trạm khỏi tuyến.');
      await loadStops();
    } catch (error) {
      message.error((error as AppError).customMessage || 'Gỡ trạm khỏi tuyến thất bại.');
    }
  };

  // Trạm chưa nằm trên tuyến này — gán rồi thì không hiện lại trong ô chọn nữa, vì
  // backend chặn trùng bằng 409 (unique index trên (RouteId, StopId)).
  const assignedStopIds = new Set(savedStops.map((stop) => stop.stopId));
  const availableStopOptions = stopOptions
    .filter((stop) => !assignedStopIds.has(stop.id))
    .map((stop) => ({ value: stop.id, label: `${stop.name} — ${stop.address}` }));

  // Còn thứ tự chưa lưu thì khoá ô gán trạm: gán xong phải nạp lại danh sách, mà nạp lại
  // là mất thứ tự vừa sắp.
  const assignLocked = !selectedRouteId || isDirty;

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Gán trạm vào tuyến
        </Title>
        <Text type="secondary">
          Chọn tuyến, kéo-thả để sắp thứ tự xe chạy qua các trạm, rồi bấm Lưu thứ tự.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Select
            placeholder="Chọn tuyến"
            style={{ minWidth: 320 }}
            value={selectedRouteId}
            loading={loadingRoutes}
            onChange={handleRouteChange}
            options={routes.map((route) => ({
              value: route.id,
              label: `${route.code} — ${route.name}`,
            }))}
          />
          <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
            Làm mới
          </Button>
        </Space>

        <Space wrap size="middle" style={{ marginBottom: 16 }}>
          <Select
            showSearch
            allowClear
            placeholder="Chọn trạm để gán vào tuyến"
            style={{ minWidth: 320 }}
            value={pendingStopId}
            loading={loadingStopOptions}
            disabled={assignLocked}
            optionFilterProp="label"
            onChange={(value) => setPendingStopId(value)}
            options={availableStopOptions}
            notFoundContent={
              loadingStopOptions ? <Spin size="small" /> : 'Không còn trạm nào để gán'
            }
          />
          <InputNumber
            placeholder="Khoảng cách (km)"
            style={{ width: 170 }}
            min={0}
            max={9999.99}
            step={0.1}
            value={pendingDistanceKm}
            disabled={assignLocked}
            onChange={(value) => setPendingDistanceKm(value ?? undefined)}
          />
          <Button
            type="primary"
            icon={<PlusOutlined />}
            loading={assigning}
            disabled={assignLocked || !pendingStopId}
            onClick={handleAssign}
          >
            Gán vào cuối tuyến
          </Button>
          {isDirty && (
            <Text type="warning">Lưu thứ tự trước khi gán thêm trạm.</Text>
          )}
        </Space>

        {!selectedRouteId && !loadingRoutes ? (
          <Empty description="Chưa có tuyến nào để gán trạm." />
        ) : loadingStops ? (
          <div style={{ textAlign: 'center', padding: '40px 0' }}>
            <Spin />
          </div>
        ) : (
          <>
            <RouteStopBoard
              stops={boardStops}
              disabled={saving}
              onReorder={setBoardStops}
              onRemove={handleRemove}
            />

            {boardStops.length > 0 && (
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'flex-end',
                  alignItems: 'center',
                  gap: 12,
                  marginTop: 16,
                }}
              >
                {isDirty && <Text type="warning">Thứ tự vừa sắp chưa được lưu.</Text>}
                <Button
                  type="primary"
                  icon={<SaveOutlined />}
                  loading={saving}
                  disabled={!isDirty}
                  onClick={handleSave}
                >
                  Lưu thứ tự
                </Button>
              </div>
            )}
          </>
        )}
      </Card>
    </div>
  );
};

export default RouteStopsPage;
