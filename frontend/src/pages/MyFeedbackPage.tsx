import { useEffect, useState } from 'react';
import { Card, Rate, Segmented, Space, Spin, Table, Tag, Typography, message } from 'antd';
import type { TableProps } from 'antd';
import { MessageOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import feedbackApi from '../api/feedbackApi';
import type { FeedbackStatus, MyFeedback, MyFeedbackDetail } from '../api/feedbackApi';
import { FEEDBACK_STATUS_OPTIONS, getFeedbackTypeMeta } from '../api/feedbackApi';
import type { AppError } from '../api/axiosClient';
import FeedbackStatusTag from '../components/FeedbackStatusTag';

const { Title, Text, Paragraph } = Typography;

/** Giá trị bộ lọc trạng thái — 'all' nghĩa là không lọc. */
type StatusFilter = 'all' | FeedbackStatus;

const STATUS_FILTER_OPTIONS = [{ label: 'Tất cả', value: 'all' as const }, ...FEEDBACK_STATUS_OPTIONS];

/**
 * Luồng phản hồi của một phản ánh — nạp chi tiết khi hành khách mở rộng dòng.
 *
 * Danh sách chỉ có `replyCount` (contract cố ý không mang `replies` theo từng dòng), nên
 * nội dung phản hồi phải gọi `GET /feedbacks/me/{id}` — chỉ nạp khi thực sự mở rộng, không
 * kéo chi tiết của mọi dòng.
 */
function FeedbackReplies({ feedbackId }: { feedbackId: string }) {
  const [detail, setDetail] = useState<MyFeedbackDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    // oxlint-disable-next-line react/set-state-in-effect
    setLoading(true);
    feedbackApi
      .getMine(feedbackId)
      .then((item) => {
        if (cancelled) return;
        setDetail(item);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const appError = err as AppError;
        setError(appError.customMessage || 'Không tải được phản hồi.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [feedbackId]);

  if (loading) return <Spin size="small" style={{ marginTop: 8 }} />;

  if (error) {
    return (
      <Paragraph type="danger" style={{ margin: '8px 0 0' }}>
        {error}
      </Paragraph>
    );
  }

  if (!detail || detail.replies.length === 0) {
    return (
      <Paragraph type="secondary" style={{ margin: '8px 0 0' }}>
        Nhà xe chưa phản hồi.
      </Paragraph>
    );
  }

  return (
    <>
      {detail.replies.map((reply) => (
        <div key={reply.id} style={{ marginTop: 8 }}>
          <Space size={8}>
            <Text strong>{reply.userFullName ?? 'Nhà xe'}</Text>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {dayjs(reply.createdAt).format('HH:mm DD/MM/YYYY')}
            </Text>
          </Space>
          <Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{reply.content}</Paragraph>
        </div>
      ))}
    </>
  );
}

// Màn hình "Phản ánh của tôi" (story 24): danh sách phản ánh của chính người đang đăng
// nhập kèm trạng thái xử lý của nhà xe. Đây là màn hình cho HÀNH KHÁCH — không giới hạn
// vai trò như các màn hình quản trị. Dữ liệu thật từ GET /feedbacks/me; mở rộng một dòng
// để xem luồng phản hồi (GET /feedbacks/me/{id}).
export default function MyFeedbackPage() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all');
  const [feedbacks, setFeedbacks] = useState<MyFeedback[]>([]);
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

  const columns: TableProps<MyFeedback>['columns'] = [
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
      render: (type: MyFeedback['type']) => {
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
      width: 260,
      render: (_, record) => {
        if (!record.routeName) return <Text type="secondary">Không kèm chuyến</Text>;
        return (
          <div>
            <div style={{ fontWeight: 600 }}>
              {record.routeCode && <Tag style={{ marginRight: 6 }}>{record.routeCode}</Tag>}
              {record.routeName}
            </div>
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
      title: 'Phản hồi',
      dataIndex: 'replyCount',
      key: 'replyCount',
      width: 110,
      align: 'center',
      render: (replyCount: number) =>
        replyCount === 0 ? (
          <Text type="secondary">—</Text>
        ) : (
          <Tag color="blue" icon={<MessageOutlined />}>
            {replyCount}
          </Tag>
        ),
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

        <Table<MyFeedback>
          rowKey="id"
          columns={columns}
          dataSource={feedbacks}
          loading={loading}
          pagination={false}
          scroll={{ x: 1100 }}
          locale={{ emptyText: 'Không có phản ánh nào' }}
          expandable={{
            expandedRowRender: (record) => (
              <div style={{ padding: '4px 8px' }}>
                <Paragraph style={{ margin: 0, whiteSpace: 'pre-wrap' }}>{record.content}</Paragraph>

                <div style={{ marginTop: 12 }}>
                  <Text strong>Phản hồi từ nhà xe</Text>
                  <FeedbackReplies feedbackId={record.id} />
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
