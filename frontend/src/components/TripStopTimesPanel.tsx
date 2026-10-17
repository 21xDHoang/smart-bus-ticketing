import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Button,
  Empty,
  Modal,
  Skeleton,
  Space,
  Tag,
  Timeline,
  TimePicker,
  Tooltip,
  message,
} from 'antd';
import { EditOutlined, ReloadOutlined } from '@ant-design/icons';
import dayjs, { type Dayjs } from 'dayjs';
import type { Trip } from '../api/tripApi';
import type { AppError } from '../api/axiosClient';
import {
  fetchTripStopTimes,
  updateTripStopTimes,
  type TripStopTime,
  type TripStopTimesResponse,
  type UpdateTripStopTimeItem,
} from '../api/tripStopTimeApi';
import { formatTime } from './ui/format';

// -----------------------------------------------------------------------------
// Dòng thời gian giờ đến của MỘT chuyến — mở rộng từ bảng Quản lý lịch trình.
//
// Giờ dự kiến do backend suy ra từ km các chặng (RouteStops.DistanceKm); quản trị
// viên chỉ sửa các trạm TRUNG GIAN. Trạm đầu / bến cuối là mốc neo của chuyến —
// sửa ở form Sửa chuyến, không sửa ở đây.
//
// Component chỉ được mount khi người dùng mở rộng dòng (expandableRowRender), nên
// mỗi chuyến chỉ gọi GET khi thật sự cần xem.
// -----------------------------------------------------------------------------

/** Múi giờ Việt Nam cố định — cùng quy ước TripScheduleFormModal. */
const VIETNAM_OFFSET = '+07:00';

interface TripStopTimesPanelProps {
  routeId: string;
  trip: Trip;
}

/** Một chuyến chỉ sửa được giờ trạm khi chưa huỷ và chưa chạy xong. */
function coTheSua(trip: Trip): boolean {
  return trip.status === 'Scheduled' || trip.status === 'Running';
}

function formatKm(km: number): string {
  return `${km.toLocaleString('vi-VN', { maximumFractionDigits: 2 })} km`;
}

/**
 * Ghép giờ người dùng chọn với NGÀY của chuyến, gửi kèm +07:00.
 *
 * Chỉ lấy phần HH:mm từ TimePicker; ngày lấy từ giờ khởi hành — chuyến của dự án
 * đều chạy trong ngày, và backend vẫn kiểm thứ tự nên giờ lỡ nằm ngoài khoảng
 * (khởi hành → bến cuối) sẽ bị trả 400 chứ không âm thầm lưu sai.
 */
function toVietnamIso(tripDate: Dayjs, time: Dayjs): string {
  return `${tripDate.format('YYYY-MM-DD')}T${time.format('HH:mm')}:00${VIETNAM_OFFSET}`;
}

export default function TripStopTimesPanel({ routeId, trip }: TripStopTimesPanelProps) {
  const [data, setData] = useState<TripStopTimesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [editOpen, setEditOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  // stopId → giờ muốn ghi đè. Rỗng = theo giờ dự kiến của trạm đó.
  const [draft, setDraft] = useState<Record<string, Dayjs | null>>({});

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(null);

    try {
      const result = await fetchTripStopTimes(routeId, trip.id);
      setData(result);
    } catch (error) {
      setLoadError((error as AppError).customMessage || 'Không tải được giờ đến từng trạm.');
    } finally {
      setLoading(false);
    }
  }, [routeId, trip.id]);

  useEffect(() => {
    void load();
  }, [load]);

  const moSuaGio = () => {
    if (!data) return;

    // Chỉ nạp sẵn các mốc ĐANG ghi đè. Trạm đang theo giờ dự kiến để trống —
    // nếu nạp sẵn giờ dự kiến rồi lưu, mọi trạm sẽ vô tình thành "sửa tay".
    const next: Record<string, Dayjs | null> = {};
    for (const stop of data.stops) {
      if (stop.isFirst || stop.isLast) continue;
      next[stop.stopId] = stop.isManual && stop.arrivalTime ? dayjs(stop.arrivalTime) : null;
    }

    setDraft(next);
    setSaveError(null);
    setEditOpen(true);
  };

  const luuGio = async () => {
    if (!data) return;

    const tripDate = dayjs(data.departureTime);
    const items: UpdateTripStopTimeItem[] = data.stops
      .filter((stop) => !stop.isFirst && !stop.isLast)
      .filter((stop) => draft[stop.stopId])
      .map((stop) => ({
        stopId: stop.stopId,
        arrivalTime: toVietnamIso(tripDate, draft[stop.stopId]!),
      }));

    setSaving(true);
    setSaveError(null);

    try {
      // Thay thế toàn phần: trạm để trống không gửi lên → mốc ghi đè cũ (nếu có) bị xoá.
      const result = await updateTripStopTimes(routeId, trip.id, items);
      setData(result);
      setEditOpen(false);
      message.success('Đã lưu giờ đến từng trạm.');
    } catch (error) {
      // Giữ modal mở để người dùng sửa lại — lỗi thứ tự / trạm ngoài tuyến hiện ngay trong modal.
      setSaveError((error as AppError).customMessage || 'Không lưu được giờ đến từng trạm.');
    } finally {
      setSaving(false);
    }
  };

  if (!data && loading) {
    return <Skeleton active paragraph={{ rows: 4 }} />;
  }

  if (!data && loadError) {
    return (
      <Alert
        type="error"
        showIcon
        message="Không tải được giờ đến từng trạm"
        description={loadError}
        action={
          <Button size="small" onClick={() => void load()}>
            Thử lại
          </Button>
        }
      />
    );
  }

  if (!data) return null;

  const trungGian = data.stops.filter((stop) => !stop.isFirst && !stop.isLast);
  const editDisabledReason = !coTheSua(trip)
    ? 'Chuyến đã huỷ hoặc đã chạy xong nên không sửa được giờ trạm.'
    : trungGian.length === 0
      ? 'Tuyến chỉ có trạm đầu và bến cuối — không có trạm trung gian để sửa.'
      : undefined;

  const timelineItems = data.stops.map((stop) => ({
    color: stop.isManual ? 'orange' : stop.isLast ? 'green' : stop.isFirst ? 'blue' : 'gray',
    children: <DongTram stop={stop} estimated={stop.isLast && data.arrivalTimeIsEstimated} />,
  }));

  return (
    <div>
      <div
        style={{
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 12,
          flexWrap: 'wrap',
          marginBottom: 12,
        }}
      >
        <Space wrap size={8}>
          <span style={{ fontWeight: 600 }}>Giờ đến từng trạm</span>
          {data.totalDistanceKm > 0 && <Tag>{formatKm(data.totalDistanceKm)}</Tag>}
        </Space>

        <Space>
          <Button
            size="small"
            icon={<ReloadOutlined />}
            loading={loading}
            onClick={() => void load()}
          >
            Tải lại
          </Button>
          <Tooltip title={editDisabledReason}>
            <Button
              size="small"
              type="primary"
              ghost
              icon={<EditOutlined />}
              disabled={editDisabledReason !== undefined}
              onClick={moSuaGio}
            >
              Sửa giờ trạm
            </Button>
          </Tooltip>
        </Space>
      </div>

      {!data.hasDistanceData && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message="Tuyến chưa có đủ khoảng cách giữa các trạm"
          description="Chưa tính được giờ dự kiến cho các trạm trung gian. Nhập km ở màn Gán trạm vào tuyến, hoặc sửa giờ tay bên dưới."
        />
      )}

      {data.arrivalTimeIsEstimated && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message="Bến cuối đang dùng giờ ước lượng từ chiều dài tuyến (25 km/h)"
          description="Muốn chốt giờ đến thật, sửa giờ chuyến ở nút Sửa của dòng."
        />
      )}

      {data.stops.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Tuyến chưa gán trạm nào." />
      ) : (
        <Timeline items={timelineItems} />
      )}

      <Modal
        title="Sửa giờ đến từng trạm"
        open={editOpen}
        onOk={luuGio}
        onCancel={() => setEditOpen(false)}
        confirmLoading={saving}
        okText="Lưu giờ trạm"
        cancelText="Huỷ"
        width={560}
        maskClosable={false}
      >
        <p style={{ color: '#666', marginTop: 0 }}>
          Chỉ áp dụng cho chuyến này. Trạm để trống sẽ theo giờ dự kiến từ km; trạm đầu và bến cuối
          sửa ở form Sửa chuyến.
        </p>

        {saveError && (
          <Alert type="error" showIcon message={saveError} style={{ marginBottom: 12 }} />
        )}

        {trungGian.map((stop) => (
          <div
            key={stop.stopId}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 12,
              padding: '8px 0',
              borderBottom: '1px solid #f0f0f0',
            }}
          >
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontWeight: 500 }}>
                {stop.stopOrder}. {stop.stopName}
              </div>
              <div style={{ fontSize: 12, color: '#888' }}>
                {stop.derivedArrivalTime
                  ? `Dự kiến ${formatTime(stop.derivedArrivalTime)}`
                  : 'Chưa có giờ dự kiến'}
                {stop.isManual && stop.arrivalTime
                  ? ` · đang ghi đè ${formatTime(stop.arrivalTime)}`
                  : ''}
              </div>
            </div>

            <TimePicker
              value={draft[stop.stopId] ?? null}
              format="HH:mm"
              minuteStep={1}
              showNow={false}
              allowClear
              placeholder="Theo dự kiến"
              style={{ width: 130 }}
              onChange={(value) =>
                setDraft((current) => ({ ...current, [stop.stopId]: value }))
              }
            />
          </div>
        ))}
      </Modal>
    </div>
  );
}

/** Một dòng trên Timeline — giờ đang dùng, tên trạm, nhãn và km cộng dồn. */
function DongTram({ stop, estimated }: { stop: TripStopTime; estimated: boolean }) {
  return (
    <div>
      <Space size={8} wrap>
        <span
          style={{
            fontWeight: 600,
            fontVariantNumeric: 'tabular-nums',
            color: stop.arrivalTime ? undefined : '#999',
          }}
        >
          {stop.arrivalTime ? formatTime(stop.arrivalTime) : '—'}
        </span>

        <span style={{ fontWeight: stop.isLast ? 600 : 500 }}>{stop.stopName}</span>

        {stop.isFirst && <Tag color="blue">Trạm đầu</Tag>}
        {stop.isLast && <Tag color="green">Bến cuối</Tag>}
        {estimated && <Tag color="gold">Ước lượng</Tag>}
        {stop.isManual && <Tag color="orange">Sửa tay</Tag>}

        {stop.isManual && stop.derivedArrivalTime && (
          <span style={{ fontSize: 12, color: '#888' }}>
            Dự kiến {formatTime(stop.derivedArrivalTime)}
          </span>
        )}
      </Space>

      <div style={{ fontSize: 12, color: '#888', marginTop: 2 }}>
        {stop.cumulativeDistanceKm > 0 && `${formatKm(stop.cumulativeDistanceKm)} · `}
        {stop.stopAddress}
      </div>
    </div>
  );
}
