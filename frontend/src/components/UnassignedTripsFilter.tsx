import { Switch, Tooltip } from 'antd';

interface UnassignedTripsFilterProps {
  /** true = chỉ hiện chuyến chưa phân công tài xế. */
  value: boolean;
  onChange: (value: boolean) => void;
}

/**
 * Bộ lọc "chỉ chuyến chưa phân công" — task 120, Dương Thị Hạnh.
 *
 * Lọc phía client trên dữ liệu đã tải về: TripResponse hiện chưa trả driverId (chờ task 113
 * của Kiên) nên chưa thể đẩy xuống query string `unassigned=true` của GET /routes/{routeId}/trips.
 * Khi backend có tham số đó thì chuyển lọc sang server-side và bỏ component lọc client này.
 */
export default function UnassignedTripsFilter({ value, onChange }: UnassignedTripsFilterProps) {
  return (
    <Tooltip title="Chỉ hiện chuyến chưa có tài xế — chuyến đã phân công sẽ bị ẩn.">
      <div style={{ display: 'inline-flex', alignItems: 'center', gap: 8 }}>
        <Switch checked={value} onChange={onChange} />
        <span>Chỉ chuyến chưa phân công</span>
      </div>
    </Tooltip>
  );
}
