import { useCallback, useEffect, useState } from 'react';
import {
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  Segmented,
  Select,
  Space,
  Tag,
  Typography,
  Upload,
  message,
} from 'antd';
import type { UploadFile } from 'antd';
import { CheckCircleFilled, EnvironmentOutlined, UploadOutlined } from '@ant-design/icons';
import dayjs from 'dayjs';
import feedbackSubmitApi from '../api/feedbackSubmitApi';
import type {
  FeedbackTripOption,
  FeedbackType,
  SubmitFeedbackPayload,
  SubmittedFeedback,
} from '../api/feedbackSubmitApi';
import { FEEDBACK_STATUS_META, FEEDBACK_TYPE_OPTIONS, getFeedbackTypeMeta } from '../api/feedbackSubmitApi';
import type { AppError } from '../api/axiosClient';
import SatisfactionRating from '../components/SatisfactionRating';

const { Title, Text, Paragraph } = Typography;

/** Giá trị của form gửi phản ánh — `tripId`/`rating` bỏ trống nghĩa là không chọn. */
interface FeedbackFormValues {
  type: FeedbackType;
  tripId?: string;
  /** `null` khi hành khách đã chấm rồi lại xoá — SatisfactionRating trả thẳng `null`. */
  rating?: number | null;
  content: string;
}

/** Số ký tự tối đa của nội dung phản ánh — khớp ràng buộc cột text của backend. */
const CONTENT_MAX_LENGTH = 2000;

// Màn hình "Gửi phản ánh / đánh giá chuyến đi" (story 24, task của Dương Thị Hạnh): hành
// khách chọn loại phản ánh, có thể gắn với một chuyến đã đi, chấm sao mức độ hài lòng, viết
// nội dung và đính kèm ảnh rồi gửi. Đây là màn hình cho HÀNH KHÁCH — không giới hạn vai trò
// như các màn hình quản trị. Backend gửi phản ánh chưa có nên đang chạy trên dữ liệu giả
// (xem feedbackSubmitApi.ts); khi API xong chỉ cần đổi cờ USE_MOCK_DATA trong file đó.
export default function FeedbackSubmitPage() {
  const [form] = Form.useForm<FeedbackFormValues>();

  const [trips, setTrips] = useState<FeedbackTripOption[]>([]);
  const [loadingTrips, setLoadingTrips] = useState(true);

  const [submitting, setSubmitting] = useState(false);
  // Phản ánh vừa gửi thành công — null khi chưa gửi hoặc đang gửi phản ánh mới.
  const [submitted, setSubmitted] = useState<SubmittedFeedback | null>(null);

  // Ảnh đính kèm — chỉ giữ trên client để xem trước, chưa upload (backend chưa có endpoint
  // upload ảnh). Khi gửi, tạm gửi tên file; luồng upload thật sẽ nối khi Hiếu bổ sung.
  const [fileList, setFileList] = useState<UploadFile[]>([]);

  const loadTrips = useCallback(async () => {
    try {
      setTrips(await feedbackSubmitApi.listMyRecentTrips());
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không tải được danh sách chuyến.');
    } finally {
      setLoadingTrips(false);
    }
  }, []);

  useEffect(() => {
    // oxlint-disable-next-line react/set-state-in-effect
    void loadTrips();
  }, [loadTrips]);

  const handleSubmit = async ({ tripId, type, content, rating }: FeedbackFormValues) => {
    setSubmitting(true);
    try {
      const payload: SubmitFeedbackPayload = {
        tripId: tripId ?? null,
        type,
        content,
        // `rating` là null khi hành khách xoá chấm hoặc chưa chạm vào ô chấm sao. Việc đổi số
        // 0 của antd Rate thành null nay nằm trong SatisfactionRating, không lặp lại ở đây.
        rating: rating ?? null,
        attachmentUrl: fileList[0]?.name ?? null,
      };
      setSubmitted(await feedbackSubmitApi.submit(payload));
      message.success('Đã gửi phản ánh thành công.');
    } catch (error) {
      message.error((error as AppError).customMessage || 'Không thể gửi phản ánh.');
    } finally {
      setSubmitting(false);
    }
  };

  // Gửi thêm một phản ánh khác: xoá kết quả cũ và đưa form về trạng thái ban đầu.
  const handleSubmitAnother = () => {
    setSubmitted(null);
    setFileList([]);
    form.resetFields();
  };

  const submittedTrip = trips.find((trip) => trip.id === submitted?.tripId);

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Title level={4} style={{ margin: 0 }}>
          Gửi phản ánh
        </Title>
        <Text type="secondary">
          Gửi khiếu nại hoặc đánh giá chất lượng chuyến đi để nhà xe cải thiện dịch vụ.
        </Text>
      </div>

      <Card
        variant="borderless"
        style={{ borderRadius: 16, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
      >
        <Form<FeedbackFormValues> form={form} layout="vertical" onFinish={handleSubmit}>
          <Form.Item
            name="type"
            label="Loại phản ánh"
            rules={[{ required: true, message: 'Vui lòng chọn loại phản ánh.' }]}
          >
            <Segmented options={FEEDBACK_TYPE_OPTIONS} />
          </Form.Item>

          <Form.Item
            name="tripId"
            label="Chuyến xe"
            extra="Bỏ trống nếu phản ánh chung về tuyến, giá vé hoặc ứng dụng."
          >
            <Select
              placeholder="Chọn chuyến đã đi (không bắt buộc)"
              loading={loadingTrips}
              allowClear
              showSearch
              optionFilterProp="label"
              options={trips.map((trip) => ({
                value: trip.id,
                label: `${trip.routeCode} — ${trip.routeName} · ${dayjs(trip.departureTime).format(
                  'HH:mm DD/MM/YYYY',
                )}`,
              }))}
              suffixIcon={<EnvironmentOutlined />}
            />
          </Form.Item>

          <Form.Item name="rating" label="Mức độ hài lòng">
            <SatisfactionRating />
          </Form.Item>

          <Form.Item
            name="content"
            label="Nội dung phản ánh"
            rules={[
              { required: true, message: 'Vui lòng nhập nội dung phản ánh.' },
              { max: CONTENT_MAX_LENGTH, message: `Nội dung tối đa ${CONTENT_MAX_LENGTH} ký tự.` },
            ]}
          >
            <Input.TextArea
              rows={4}
              maxLength={CONTENT_MAX_LENGTH}
              showCount
              placeholder="Mô tả vấn đề, góp ý hoặc cảm nhận của bạn về chuyến đi…"
            />
          </Form.Item>

          <Form.Item label="Ảnh đính kèm (không bắt buộc)">
            <Upload
              listType="picture"
              maxCount={1}
              fileList={fileList}
              beforeUpload={() => false}
              onChange={({ fileList: next }) => setFileList(next.slice(-1))}
              onRemove={() => setFileList([])}
            >
              <Button icon={<UploadOutlined />}>Chọn ảnh</Button>
            </Upload>
          </Form.Item>

          <Button type="primary" htmlType="submit" size="large" loading={submitting}>
            Gửi phản ánh
          </Button>
        </Form>
      </Card>

      {/* Kết quả gửi thành công. */}
      {submitted && (
        <Card
          variant="borderless"
          style={{ borderRadius: 16, marginTop: 20, boxShadow: '0 4px 12px rgba(0,0,0,0.03)' }}
          title={
            <Space>
              <CheckCircleFilled style={{ color: '#52c41a' }} />
              <span>Gửi phản ánh thành công</span>
            </Space>
          }
        >
          <Descriptions
            column={1}
            size="middle"
            items={[
              {
                key: 'type',
                label: 'Loại phản ánh',
                children: (
                  <Tag color={getFeedbackTypeMeta(submitted.type).color}>
                    {getFeedbackTypeMeta(submitted.type).label}
                  </Tag>
                ),
              },
              {
                key: 'trip',
                label: 'Chuyến xe',
                children: submittedTrip
                  ? `${submittedTrip.routeCode} — ${submittedTrip.routeName} · ${dayjs(
                      submittedTrip.departureTime,
                    ).format('HH:mm DD/MM/YYYY')}`
                  : 'Không kèm chuyến',
              },
              {
                key: 'rating',
                label: 'Mức độ hài lòng',
                children: <SatisfactionRating readOnly value={submitted.rating} />,
              },
              {
                key: 'status',
                label: 'Trạng thái',
                children: (
                  <Tag color={FEEDBACK_STATUS_META[submitted.status].color}>
                    {FEEDBACK_STATUS_META[submitted.status].label}
                  </Tag>
                ),
              },
            ]}
          />

          <Paragraph style={{ marginTop: 12, whiteSpace: 'pre-wrap' }}>{submitted.content}</Paragraph>

          <Text type="secondary" style={{ fontSize: 12 }}>
            Gửi lúc {dayjs(submitted.createdAt).format('HH:mm DD/MM/YYYY')}. Nhà xe sẽ phản hồi
            trong thời gian sớm nhất.
          </Text>

          <Button style={{ marginTop: 16 }} onClick={handleSubmitAnother}>
            Gửi phản ánh khác
          </Button>
        </Card>
      )}
    </div>
  );
}
