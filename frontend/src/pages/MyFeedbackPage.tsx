import { useEffect, useState } from 'react';
import { Card, Rate, Segmented, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import dayjs from 'dayjs';
import feedbackApi from '../api/feedbackApi';
import type { Feedback, FeedbackStatus } from '../api/feedbackApi';
import { FEEDBACK_STATUS_OPTIONS, getFeedbackTypeMeta } from '../api/feedbackApi';
import type { AppError } from '../api/axiosClient';
import FeedbackStatusTag from '../components/FeedbackStatusTag';

const { Title, Text, Paragraph } = Typography;

/** Giá trị bộ lọc trạng thái — 'all' nghĩa là không lọc. */
type StatusFilter = 'all' | FeedbackStatus;

const STATUS_FILTER_OPTIONS = [{ label: 'Tất cả', value: 'all' as const }, ...FEEDBACK_STATUS_OPTIONS];

// Màn hình "Phản ánh của tôi" (story 24): danh sách phản ánh của chính người đang đăng
// nhập kèm trạng thái xử lý của nhà xe. Đây là màn hình cho HÀNH KHÁCH — không giới hạn
// vai trò như các màn hình quản trị. Backend phản ánh chưa có nên đang chạy trên dữ liệu
// giả (xem feedbackApi.ts); khi API xong chỉ cần đổi cờ USE_MOCK_DATA trong file đó.
export default function MyFeedbackPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [feedbacks, setFeedbacks] = useState<Feedback[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    // Bật spinner khi bắt đầu tải. Đây là lần tải thực sự từ API (không phải "đồng bộ
    // state dẫn xuất") nên tắt cảnh báo react/set-state-in-effect cho đúng ngữ cảnh.
    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    feedbackApi
      .listMyFeedbacks(statusFilter === 'all' ? undefined : statusFilter)
      .then((list) => {
        if (cancelled) return;
        setFeedbacks(list);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        message.error(appError.customMessage || 'Không thể tải danh sách phản ánh.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [statusFilter]);

  const columns: TableProps<Feedback>['columns'] = [
    {
      title: 'Ngày gửi',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 160,
      render: (createdAt: string) => dayjs(createdAt).format('DD/MM/YYYY HH:mm'),
    },
    {
      title: 'Loại phản ánh',
      dataIndex: 'type',
      key: 'type',
      width: 130,
      render: (type: Feedback['type']) => {
        const meta = getFeedbackTypeMeta(type);
        return <Tag color={meta.color}>{meta.label}</Tag>;
      },
    },
    {
      title: 'Nội dung',
      dataIndex: 'content',
      key: 'content',
      render: (content: string) => (
        <Paragraph style={{ margin: 0 }} ellipsis={{ rows: 2, tooltip: content }}>
          {content}
        </Paragraph>
      ),
    },
    {
      title: 'Chuyến xe',
      key: 'trip',
      width: 240,
      render: (_, record) => {
        if (!record.routeName) return <Text type="secondary">Không kèm chuyến</Text>;
        return (
          <div>
            <div style={{ fontWeight: 600 }}>{record.routeName}</div>
            {record.departureTime && (
              <Text type="secondary" style={{ fontSize: 12 }}>
                {dayjs(record.departureTime).format('HH:mm DD/MM/YYYY')}
              </Text>
            )}
          </div>
        );
      },
    },
    {
      title: 'Mức độ hài lòng',
      dataIndex: 'rating',
      key: 'rating',
      width: 170,
      render: (rating: number | null) =>
        rating === null ? <Text type="secondary">—</Text> : <Rate disabled value={rating} />,
    },
    {
      title: 'Trạng thái xử lý',
      dataIndex: 'status',
      key: 'status',
      width: 150,
      render: (status: FeedbackStatus) => <FeedbackStatusTag status={status} />,
    },
  ];

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Phản ánh của tôi
        </Title>
        <Text type="secondary">
          Theo dõi các khiếu nại, đánh giá bạn đã gửi và trạng thái xử lý của nhà xe.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Segmented
          options={STATUS_FILTER_OPTIONS}
          value={statusFilter}
          onChange={(value) => setStatusFilter(value as StatusFilter)}
          style={{ marginBottom: 16 }}
        />

        <Table<Feedback>
          rowKey="id"
          columns={columns}
          dataSource={feedbacks}
          loading={loading}
          pagination={false}
          scroll={{ x: 980 }}
          locale={{ emptyText: 'Không có phản ánh nào' }}
          expandable={{
            expandedRowRender: (record) => (
              <div style={{ padding: '4px 8px' }}>
                <Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{record.content}</Paragraph>

                <div style={{ marginTop: 12 }}>
                  <Text strong>Phản hồi từ nhà xe: </Text>
                  {record.adminReply ? (
                    <Text>{record.adminReply}</Text>
                  ) : (
                    <Text type="secondary">Chưa có phản hồi.</Text>
                  )}
                </div>

                {record.updatedAt && (
                  <Text type="secondary" style={{ display: 'block', marginTop: 8, fontSize: 12 }}>
                    Cập nhật lần cuối {dayjs(record.updatedAt).format('DD/MM/YYYY HH:mm')}
                  </Text>
                )}
              </div>
            ),
          }}
        />
      </Card>
    </div>
  );
}
