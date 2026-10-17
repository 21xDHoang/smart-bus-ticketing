import { useEffect, useState } from 'react';
import { Space, Spin, Typography } from 'antd';
import { fetchDriverAssignment } from '../api/adminUserApi';
import type { DriverAssignment } from '../api/adminUserApi';
import { TRIP_STATUS_META } from '../api/tripApi';
import { formatDateTime } from './ui/format';
import { RouteBadge, StatusTag } from './ui';

/**
 * Phân công hiện tại của một tài xế: chuyến đang chạy, nếu không có thì chuyến kế tiếp
 * (xe + tuyến + giờ khởi hành). Dữ liệu lấy từ GET /drivers/{id}/trips — xem
 * fetchDriverAssignment trong api/adminUserApi.ts.
 *
 * Ô này tự tải dữ liệu để dùng được cả trong bảng (mỗi dòng tài xế một lần gọi) lẫn trong
 * modal sửa tài khoản, không buộc trang cha phải gom trước.
 */
export default function DriverAssignmentInfo({
  driverId,
  compact = false,
}: {
  driverId: string;
  /** true = bố cục chật cho ô trong bảng; false = có nhãn từng dòng cho modal. */
  compact?: boolean;
}) {
  const [assignment, setAssignment] = useState<DriverAssignment | null>(null);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;

    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    // oxlint-disable-next-line react/set-state-in-effect
    setFailed(false);

    fetchDriverAssignment(driverId)
      .then((result) => {
        if (!cancelled) setAssignment(result);
      })
      .catch(() => {
        if (cancelled) return;
        // Không toast: ô này nằm trong bảng nhiều dòng, một dòng lỗi mạng không nên bắn
        // toast chồng lên thao tác chính của người dùng.
        setAssignment(null);
        setFailed(true);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [driverId]);

  if (loading) {
    return (
      <Space size={6}>
        <Spin size="small" />
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Đang tải phân công…
        </Typography.Text>
      </Space>
    );
  }

  if (failed) {
    return (
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        Không tải được phân công
      </Typography.Text>
    );
  }

  if (!assignment) {
    return (
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        Chưa được phân công chuyến nào
      </Typography.Text>
    );
  }

  const statusMeta = TRIP_STATUS_META[assignment.status];

  if (compact) {
    return (
      <Space orientation="vertical" size={2}>
        <Space size={6} wrap>
          <RouteBadge code={assignment.routeCode} size="sm" />
          <Typography.Text style={{ fontSize: 13 }} ellipsis={{ tooltip: assignment.routeName }}>
            {assignment.routeName}
          </Typography.Text>
          <StatusTag color={statusMeta.color}>{statusMeta.label}</StatusTag>
        </Space>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Xe {assignment.busLicensePlate} · {formatDateTime(assignment.departureTime)}
        </Typography.Text>
      </Space>
    );
  }

  return (
    <Space orientation="vertical" size={8} style={{ width: '100%' }}>
      <div>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Tuyến
        </Typography.Text>
        <div>
          <Space size={6} wrap>
            <RouteBadge code={assignment.routeCode} size="sm" />
            <Typography.Text>{assignment.routeName}</Typography.Text>
          </Space>
        </div>
      </div>

      <div>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Xe đang gán
        </Typography.Text>
        <div>
          <StatusTag color="blue">{assignment.busLicensePlate}</StatusTag>
        </div>
      </div>

      <div>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Giờ khởi hành
        </Typography.Text>
        <div>{formatDateTime(assignment.departureTime)}</div>
      </div>

      <div>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          Trạng thái chuyến
        </Typography.Text>
        <div>
          <StatusTag color={statusMeta.color}>{statusMeta.label}</StatusTag>
        </div>
      </div>
    </Space>
  );
}
