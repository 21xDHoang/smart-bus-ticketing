import { useCallback, useEffect, useMemo, useState } from 'react';
import { Button, InputNumber, Modal, Select, Spin, Typography, message } from 'antd';
import { AimOutlined, PlusOutlined, ReloadOutlined, SaveOutlined } from '@ant-design/icons';
import RouteStopBoard from '../components/RouteStopBoard';
import { EmptyState, FilterBar, LoadingState, PageCard, PageHeader } from '../components/ui';
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

const { Text } = Typography;

const EARTH_RADIUS_KM = 6371;

const roundKm = (value: number) => Math.round(value * 100) / 100;

/**
 * Khoảng cách đường chim bay giữa hai trạm (km).
 *
 * Đây là giá trị ƯỚC LƯỢNG để gợi ý, không phải quãng đường xe chạy thật: đường bộ dài
 * hơn đường chim bay. Vì vậy trang chuẩn hoá tổng các đoạn về đúng `Route.DistanceKm`
 * rồi để quản lý soát lại trước khi lưu, chứ không ghi thẳng con số này xuống CSDL.
 */
const haversineKm = (from: RouteStop, to: RouteStop) => {
  const toRadians = (degrees: number) => (degrees * Math.PI) / 180;
  const deltaLat = toRadians(to.latitude - from.latitude);
  const deltaLng = toRadians(to.longitude - from.longitude);
  const a =
    Math.sin(deltaLat / 2) ** 2 +
    Math.cos(toRadians(from.latitude)) * Math.cos(toRadians(to.latitude)) * Math.sin(deltaLng / 2) ** 2;

  return 2 * EARTH_RADIUS_KM * Math.asin(Math.sqrt(a));
};

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
 * Gỡ trạm gọi API ngay (không chờ Lưu), còn đổi THỨ TỰ và KHOẢNG CÁCH thì phải bấm
 * "Lưu thứ tự & khoảng cách": thao tác sắp xếp cần một lần gửi trọn vẹn cả danh sách,
 * không phải từng dòng một.
 *
 * Khoảng cách từng đoạn là dữ liệu cho giờ đến dự kiến ở màn sinh chuyến: thiếu nó thì
 * không chia được thời gian chạy cho các trạm. Trang có nút ước lượng từ toạ độ GPS,
 * nhưng kết quả chỉ là gợi ý để soát — quản lý vẫn bấm Lưu mới ghi xuống CSDL.
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

  // Danh sách hiển thị khác bản đã lưu hay chưa — so cả thứ tự (id từng vị trí) lẫn
  // khoảng cách từng đoạn. Backend trả `stopOrder` mới sau khi lưu nên không so bằng
  // chính trường đó.
  const isDirty = useMemo(
    () =>
      savedStops.length !== boardStops.length ||
      savedStops.some((stop, index) => {
        const current = boardStops[index];
        return (
          !current ||
          stop.id !== current.id ||
          Math.abs((stop.distanceKm ?? 0) - (current.distanceKm ?? 0)) > 0.001
        );
      }),
    [savedStops, boardStops],
  );

  const selectedRoute = useMemo(
    () => routes.find((route) => route.id === selectedRouteId),
    [routes, selectedRouteId],
  );
  const routeDistanceKm = selectedRoute?.distanceKm ?? 0;

  // Tổng các đoạn đang nhập, để đối chiếu với chiều dài tuyến trước khi lưu.
  const segmentTotalKm = useMemo(
    () => roundKm(boardStops.reduce((sum, stop) => sum + (Number(stop.distanceKm) || 0), 0)),
    [boardStops],
  );
  const totalMatchesRoute = routeDistanceKm <= 0 || Math.abs(segmentTotalKm - routeDistanceKm) <= 0.01;

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
      title: 'Bỏ thay đổi chưa lưu?',
      content:
        'Thứ tự hoặc khoảng cách trạm vừa sửa chưa được lưu. Đổi tuyến sẽ bỏ các thay đổi đó.',
      okText: 'Bỏ thay đổi',
      cancelText: 'Ở lại',
      okButtonProps: { danger: true },
      onOk: () => setSelectedRouteId(value),
    });
  };

  /**
   * Đổi thứ tự trạm trên bảng.
   *
   * `DistanceKm` là khoảng cách của ĐOẠN ĐƯỜNG tại một vị trí, không bám theo trạm. Khi
   * kéo trạm sang vị trí khác, giữ nguyên dãy số theo vị trí (trạm đầu = 0) thay vì di
   * chuyển con số theo trạm — rồi nhắc quản lý kiểm tra/ước lượng lại. Tự tính lại ngay
   * lúc kéo sẽ làm thay đổi thứ tự kéo theo thay đổi số liệu mà không ai chủ ý.
   */
  const handleReorder = (nextStops: RouteStop[]) => {
    const distancesByPosition = boardStops.map((stop) => stop.distanceKm);

    setBoardStops(
      nextStops.map((stop, index) => ({
        ...stop,
        distanceKm: index === 0 ? 0 : (distancesByPosition[index] ?? 0),
      })),
    );

    message.info(
      'Đã đổi thứ tự trạm. Khoảng cách các đoạn chưa được tính lại — kiểm tra hoặc bấm "Ước lượng từ toạ độ".',
    );
  };

  const handleDistanceChange = (stopId: string, distanceKm: number) => {
    const normalized = roundKm(Math.max(0, distanceKm));

    setBoardStops((current) =>
      current.map((stop, index) =>
        index > 0 && stop.id === stopId ? { ...stop, distanceKm: normalized } : stop,
      ),
    );
  };

  /**
   * Ước lượng khoảng cách từng đoạn bằng toạ độ GPS của trạm.
   *
   * Đường chim bay ngắn hơn đường bộ, nên tổng thô được CHUẨN HOÁ về chiều dài tuyến
   * (`Route.DistanceKm`) — giữ tương quan giữa các đoạn mà tổng vẫn khớp con số quản lý
   * đã đặt. Kết quả chỉ đổ vào bảng để soát, KHÔNG tự lưu: quản lý vẫn là người bấm Lưu.
   */
  const handleEstimateDistances = () => {
    if (boardStops.length < 2) {
      message.info('Cần ít nhất hai trạm mới ước lượng được khoảng cách.');
      return;
    }

    const rawSegments = boardStops.map((stop, index) =>
      index === 0 ? 0 : haversineKm(boardStops[index - 1], stop),
    );
    const rawTotal = rawSegments.reduce((sum, km) => sum + km, 0);

    if (!(rawTotal > 0)) {
      message.warning('Các trạm đang trùng toạ độ — chưa ước lượng được khoảng cách.');
      return;
    }

    const targetKm = routeDistanceKm > 0 ? routeDistanceKm : 0;
    const factor = targetKm > 0 ? targetKm / rawTotal : 1;
    const nextDistances = rawSegments.map((km, index) =>
      index === 0 ? 0 : roundKm(km * factor),
    );

    // Dồn sai số làm tròn vào đoạn cuối để tổng khớp đúng chiều dài tuyến.
    const desiredTotalKm = targetKm > 0 ? targetKm : roundKm(rawTotal);
    const roundedTotalKm = roundKm(nextDistances.reduce((sum, km) => sum + km, 0));
    const lastIndex = nextDistances.length - 1;
    nextDistances[lastIndex] = roundKm(
      Math.max(0, nextDistances[lastIndex] + (desiredTotalKm - roundedTotalKm)),
    );

    const seenDistances = boardStops.map((stop, index) => ({
      stop,
      distanceKm: nextDistances[index] ?? 0,
    }));
    const abnormalStops = seenDistances
      .filter(
        ({ distanceKm }, index) => index > 0 && targetKm > 0 && distanceKm > targetKm,
      )
      .map(({ stop }) => stop.stopName);

    setBoardStops((current) =>
      current.map((stop, index) => ({ ...stop, distanceKm: nextDistances[index] ?? 0 })),
    );

    if (targetKm <= 0) {
      message.info(
        `Tuyến chưa có chiều dài — đã ước lượng ${desiredTotalKm.toFixed(2)} km theo đường chim bay. Soát lại rồi bấm Lưu.`,
      );
    } else if (abnormalStops.length > 0) {
      const names = abnormalStops.slice(0, 3).join(', ');
      const more = abnormalStops.length > 3 ? '…' : '';
      message.warning(
        `Đã ước lượng ${targetKm.toFixed(2)} km, nhưng ${abnormalStops.length} đoạn dài hơn cả tuyến (${names}${more}) — kiểm tra lại toạ độ hoặc số km.`,
      );
    } else {
      message.success(
        `Đã ước lượng ${targetKm.toFixed(2)} km cho ${boardStops.length - 1} đoạn. Soát lại rồi bấm Lưu.`,
      );
    }
  };

  const handleSave = async () => {
    if (!selectedRouteId) return;

    setSaving(true);
    try {
      // Gửi kèm `distanceKm` cho TỪNG dòng: bỏ trống nghĩa là giữ nguyên khoảng cách đang
      // có (docs/api-contract.md, mục PUT /routes/{routeId}/stops/order), nên muốn lưu
      // số vừa nhập/ước lượng thì phải gửi tường minh. Trạm đầu luôn 0.
      const updated = await reorderRouteStops(
        selectedRouteId,
        boardStops.map((stop, index) => ({
          stopId: stop.stopId,
          distanceKm: index === 0 ? 0 : stop.distanceKm,
        })),
      );

      setSavedStops(updated);
      setBoardStops(updated);
      message.success('Đã lưu thứ tự và khoảng cách trạm.');
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
      <PageHeader
        title="Gán trạm vào tuyến"
        subtitle="Chọn tuyến, kéo-thả để sắp thứ tự, nhập khoảng cách từng đoạn (hoặc ước lượng từ toạ độ), rồi bấm Lưu."
      />

      <PageCard
        toolbar={
          <FilterBar
            actions={
              <Button icon={<ReloadOutlined />} onClick={handleRefresh}>
                Làm mới
              </Button>
            }
          >
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
          </FilterBar>
        }
      >
        <div style={{ marginBottom: 16 }}>
          <FilterBar
            actions={
              <>
                {isDirty && (
                  <Text type="warning">Lưu thay đổi trước khi gán thêm trạm.</Text>
                )}
                {/* Nút thường — cả màn chỉ giữ một primary là "Lưu thứ tự & khoảng cách". */}
                <Button
                  icon={<PlusOutlined />}
                  loading={assigning}
                  disabled={assignLocked || !pendingStopId}
                  onClick={handleAssign}
                >
                  Gán vào cuối tuyến
                </Button>
              </>
            }
          >
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
          </FilterBar>
        </div>

        {!selectedRouteId && !loadingRoutes ? (
          <EmptyState description="Chưa có tuyến nào để gán trạm." />
        ) : loadingStops ? (
          <LoadingState />
        ) : (
          <>
            <RouteStopBoard
              stops={boardStops}
              disabled={saving}
              onReorder={handleReorder}
              onRemove={handleRemove}
              onDistanceChange={handleDistanceChange}
              routeDistanceKm={routeDistanceKm > 0 ? routeDistanceKm : undefined}
            />

            {boardStops.length > 0 && (
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  gap: 12,
                  marginTop: 16,
                  flexWrap: 'wrap',
                }}
              >
                <Text type={totalMatchesRoute ? 'secondary' : 'warning'}>
                  Tổng các đoạn: {segmentTotalKm.toFixed(2)} km
                  {routeDistanceKm > 0
                    ? ` / chiều dài tuyến: ${routeDistanceKm.toFixed(2)} km`
                    : ' (tuyến chưa có chiều dài)'}
                </Text>

                <div
                  style={{
                    display: 'flex',
                    justifyContent: 'flex-end',
                    alignItems: 'center',
                    gap: 12,
                    flexWrap: 'wrap',
                  }}
                >
                  {isDirty && <Text type="warning">Thay đổi chưa được lưu.</Text>}
                  <Button
                    icon={<AimOutlined />}
                    disabled={saving || boardStops.length < 2}
                    onClick={handleEstimateDistances}
                  >
                    Ước lượng từ toạ độ
                  </Button>
                  <Button
                    type="primary"
                    icon={<SaveOutlined />}
                    loading={saving}
                    disabled={!isDirty}
                    onClick={handleSave}
                  >
                    Lưu thứ tự &amp; khoảng cách
                  </Button>
                </div>
              </div>
            )}
          </>
        )}
      </PageCard>
    </div>
  );
};

export default RouteStopsPage;
