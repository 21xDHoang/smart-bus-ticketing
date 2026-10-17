import { useState } from 'react';
import type { DragEvent } from 'react';
import { Button, Empty, InputNumber, Popconfirm, Space, Tooltip, Typography, theme } from 'antd';
import {
  ArrowDownOutlined,
  ArrowUpOutlined,
  DeleteOutlined,
  HolderOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import type { RouteStop } from '../api/routeStopApi';
import { moveStop } from './routeStopOrder';
import StatusTag from './ui/StatusTag';

interface RouteStopBoardProps {
  /** Danh sách trạm theo thứ tự đang hiển thị — cha giữ state, board chỉ báo lên khi đổi. */
  stops: RouteStop[];

  /** Khoá thao tác trong lúc đang gọi API (lưu thứ tự / gỡ trạm). */
  disabled?: boolean;

  /** Gọi khi thứ tự đổi, kèm mảng ĐÃ sắp lại. Cha quyết định lưu hay không. */
  onReorder: (stops: RouteStop[]) => void;

  /** Gọi khi bấm nút gỡ — cha gọi API rồi nạp lại danh sách. */
  onRemove: (stop: RouteStop) => void;

  /**
   * Gọi khi sửa khoảng cách của một đoạn. Giá trị là khoảng cách từ TRẠM TRƯỚC đến trạm
   * này (trạm đầu luôn 0) — đúng nghĩa `RouteStops.DistanceKm` trong hợp đồng API.
   */
  onDistanceChange: (stopId: string, distanceKm: number) => void;

  /** Chiều dài toàn tuyến, dùng để cảnh báo đoạn dài bất thường. Bỏ trống = không cảnh báo. */
  routeDistanceKm?: number;
}

/**
 * Bảng trạm của một tuyến, sắp thứ tự bằng kéo-thả.
 *
 * Dùng kéo-thả gốc của trình duyệt (HTML5 Drag and Drop) chứ không dùng thư viện: không
 * phải thêm dependency, `package.json` giữ nguyên. Đổi lại, kéo-thả gốc KHÔNG hỗ trợ bàn
 * phím và cảm ứng — nên mỗi dòng có thêm cặp nút ↑/↓ làm đường sắp xếp thay thế. Hai
 * đường này gọi chung `moveStop` nên cho ra kết quả y hệt nhau.
 */
const RouteStopBoard = ({
  stops,
  disabled,
  onReorder,
  onRemove,
  onDistanceChange,
  routeDistanceKm,
}: RouteStopBoardProps) => {
  // Màu lấy từ token chung — sáng/tối tự đổi theo chế độ, không viết hex tay trong component.
  const { token } = theme.useToken();

  // Dòng đang được kéo và dòng đang được rê qua — chỉ để tô sáng, không phải dữ liệu thật.
  const [draggingId, setDraggingId] = useState<string | null>(null);
  const [overId, setOverId] = useState<string | null>(null);

  const handleDragStart = (event: DragEvent<HTMLDivElement>, id: string) => {
    if (disabled) return;

    setDraggingId(id);
    event.dataTransfer.effectAllowed = 'move';

    // Firefox không bắt đầu kéo nếu `setData` không được gọi, dù có `draggable`.
    event.dataTransfer.setData('text/plain', id);
  };

  const handleDragOver = (event: DragEvent<HTMLDivElement>, id: string) => {
    if (disabled || !draggingId) return;

    // Bắt buộc: không `preventDefault` thì trình duyệt coi đây không phải vùng thả được
    // và sự kiện `drop` sẽ không bao giờ bắn.
    event.preventDefault();
    event.dataTransfer.dropEffect = 'move';

    if (id !== overId) setOverId(id);
  };

  const handleDrop = (event: DragEvent<HTMLDivElement>, targetId: string) => {
    event.preventDefault();

    // `draggingId` là nguồn chính; `getData` là đường dự phòng nếu state chưa kịp cập nhật.
    const sourceId = draggingId ?? event.dataTransfer.getData('text/plain');

    setDraggingId(null);
    setOverId(null);

    if (disabled || !sourceId || sourceId === targetId) return;

    const fromIndex = stops.findIndex((stop) => stop.id === sourceId);
    const toIndex = stops.findIndex((stop) => stop.id === targetId);

    onReorder(moveStop(stops, fromIndex, toIndex));
  };

  // Huỷ kéo giữa chừng (thả ra ngoài, bấm Esc) — dọn tô sáng, không đổi gì.
  const handleDragEnd = () => {
    setDraggingId(null);
    setOverId(null);
  };

  if (stops.length === 0) {
    return <Empty description="Tuyến này chưa gán trạm nào" />;
  }

  return (
    <div>
      {stops.map((stop, index) => {
        const isDragging = stop.id === draggingId;
        const isOver = stop.id === overId && !isDragging;
        // Một đoạn dài hơn cả tuyến gần như luôn là toạ độ sai hoặc nhập nhầm đơn vị. Vẫn
        // cho lưu (quản lý có thể cố ý), nhưng tô cảnh báo ngay tại dòng để họ thấy.
        const isAbnormal =
          index > 0 &&
          routeDistanceKm !== undefined &&
          routeDistanceKm > 0 &&
          stop.distanceKm > routeDistanceKm;

        return (
          <div
            key={stop.id}
            draggable={!disabled}
            onDragStart={(event) => handleDragStart(event, stop.id)}
            onDragOver={(event) => handleDragOver(event, stop.id)}
            onDrop={(event) => handleDrop(event, stop.id)}
            onDragEnd={handleDragEnd}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 12,
              padding: '10px 12px',
              marginBottom: 8,
              borderRadius: token.borderRadius,
              border: `1px solid ${isOver ? token.colorPrimary : token.colorBorderSecondary}`,
              background: isDragging
                ? token.colorFillQuaternary
                : isOver
                  ? token.colorPrimaryBg
                  : token.colorBgContainer,
              opacity: isDragging ? 0.5 : 1,
              cursor: disabled ? 'default' : 'grab',
            }}
          >
            <Tooltip title={disabled ? undefined : 'Kéo để đổi thứ tự'}>
              <HolderOutlined style={{ color: token.colorTextTertiary, fontSize: 16 }} />
            </Tooltip>

            <StatusTag color="blue" style={{ minWidth: 32, textAlign: 'center', margin: 0 }}>
              {index + 1}
            </StatusTag>

            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ fontWeight: 600 }}>{stop.stopName}</div>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {stop.stopAddress}
              </Typography.Text>
            </div>

            {/* Khoảng cách gắn với VỊ TRÍ đoạn đường, không gắn với bản thân trạm — đổi
                thứ tự phải kiểm tra lại (xem cảnh báo ở RouteStopsPage). */}
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexShrink: 0 }}>
              {index === 0 ? (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  Điểm đầu · 0 km
                </Typography.Text>
              ) : (
                <>
                  <Tooltip title="Khoảng cách từ trạm trước đến trạm này">
                    <InputNumber
                      size="small"
                      min={0}
                      max={9999.99}
                      step={0.1}
                      precision={2}
                      value={stop.distanceKm}
                      disabled={disabled}
                      status={isAbnormal ? 'warning' : undefined}
                      style={{ width: 100 }}
                      aria-label={`Khoảng cách từ trạm trước đến ${stop.stopName} (km)`}
                      onChange={(value) => onDistanceChange(stop.id, value ?? 0)}
                    />
                  </Tooltip>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    km
                  </Typography.Text>
                  {isAbnormal && (
                    <Tooltip
                      title={`Đoạn này dài hơn cả tuyến (${routeDistanceKm} km) — kiểm tra lại toạ độ hoặc số km đã nhập.`}
                    >
                      <WarningOutlined style={{ color: token.colorWarning }} />
                    </Tooltip>
                  )}
                </>
              )}
            </div>

            <Space size={0}>
              <Button
                type="text"
                size="small"
                icon={<ArrowUpOutlined />}
                disabled={disabled || index === 0}
                onClick={() => onReorder(moveStop(stops, index, index - 1))}
                aria-label={`Đưa ${stop.stopName} lên trên`}
              />
              <Button
                type="text"
                size="small"
                icon={<ArrowDownOutlined />}
                disabled={disabled || index === stops.length - 1}
                onClick={() => onReorder(moveStop(stops, index, index + 1))}
                aria-label={`Đưa ${stop.stopName} xuống dưới`}
              />
              <Popconfirm
                title="Gỡ trạm khỏi tuyến?"
                description={`“${stop.stopName}” sẽ không còn nằm trên tuyến này.`}
                okText="Gỡ"
                cancelText="Huỷ"
                okButtonProps={{ danger: true }}
                disabled={disabled}
                onConfirm={() => onRemove(stop)}
              >
                <Button
                  type="text"
                  size="small"
                  danger
                  icon={<DeleteOutlined />}
                  disabled={disabled}
                  aria-label={`Gỡ ${stop.stopName} khỏi tuyến`}
                />
              </Popconfirm>
            </Space>
          </div>
        );
      })}
    </div>
  );
};

export default RouteStopBoard;
