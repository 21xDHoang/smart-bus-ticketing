import { Descriptions, Modal, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import type { AuditLog } from '../api/auditLogApi';
import { getAuditActionMeta } from '../api/auditLogApi';

const { Text } = Typography;

interface AuditLogDetailModalProps {
  open: boolean;
  /** Bản ghi đang xem. null = chưa chọn (modal đóng). */
  log: AuditLog | null;
  onClose: () => void;
}

// Tách chuỗi target dạng "<Tên bảng>:<Id>" để hiển thị tách bạch tên bảng và id.
// Login/Logout không có target → trả null.
function splitTarget(target: string | null): { table: string; id: string } | null {
  if (!target) return null;
  const separator = target.indexOf(':');
  if (separator === -1) return { table: target, id: '' };
  return { table: target.slice(0, separator), id: target.slice(separator + 1) };
}

// Hiển thị một GUID dạng mã; null → nhãn "Không xác định".
function GuidText({ value }: { value: string | null }) {
  if (!value) return <Text type="secondary">Không xác định</Text>;
  return <Text code>{value}</Text>;
}

// Modal hiển thị chi tiết một bản ghi nhật ký kiểm toán.
// Component thuần "trình bày": nhận nguyên bản ghi qua prop, không tự gọi API — màn hình
// danh sách (task của Hạnh) đã có sẵn bản ghi khi người dùng bấm "Chi tiết" nên không cần
// gọi thêm endpoint nào (đúng quy ước: không gọi endpoint chưa có trong api-contract.md).
export default function AuditLogDetailModal({ open, log, onClose }: AuditLogDetailModalProps) {
  const actionMeta = log ? getAuditActionMeta(log.action) : null;
  const target = log ? splitTarget(log.target) : null;
  const time = log ? dayjs(log.createdAt) : null;

  return (
    <Modal
      title="Chi tiết bản ghi nhật ký"
      open={open}
      onCancel={onClose}
      onOk={onClose}
      okText="Đóng"
      cancelButtonProps={{ style: { display: 'none' } }}
      width={680}
    >
      {log && (
        <Descriptions column={1} bordered>
          <Descriptions.Item label="Thời gian">
            {time?.isValid() ? (
              <div>
                <div>{time.format('DD/MM/YYYY HH:mm:ss')}</div>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {log.createdAt} (UTC)
                </Text>
              </div>
            ) : (
              <Text type="secondary">{log.createdAt || '—'}</Text>
            )}
          </Descriptions.Item>

          <Descriptions.Item label="Hành động">
            {actionMeta && <Tag color={actionMeta.color}>{actionMeta.label}</Tag>}
          </Descriptions.Item>

          <Descriptions.Item label="Người thao tác (ID)">
            <GuidText value={log.userId} />
          </Descriptions.Item>

          <Descriptions.Item label="Đối tượng">
            {target ? (
              <div>
                <Tag color="blue">{target.table}</Tag>
                {target.id && (
                  <Text code style={{ marginLeft: 8 }}>
                    {target.id}
                  </Text>
                )}
              </div>
            ) : (
              <Text type="secondary">—</Text>
            )}
          </Descriptions.Item>

          <Descriptions.Item label="Địa chỉ IP">
            {log.ipAddress ? (
              <Text code>{log.ipAddress}</Text>
            ) : (
              <Text type="secondary">Không xác định</Text>
            )}
          </Descriptions.Item>

          <Descriptions.Item label="Mã bản ghi">
            <Text code>{log.id}</Text>
          </Descriptions.Item>
        </Descriptions>
      )}
    </Modal>
  );
}
