import { Button, Result } from 'antd';
import { useNavigate } from 'react-router-dom';

/**
 * Màn hình 404 — đường dẫn không tồn tại. Trước đây catch-all tự đá về trang chủ, người gõ
 * nhầm URL bị đưa đi chỗ khác mà không hiểu vì sao; giờ báo rõ rồi mời họ tự chọn đường về.
 */
export default function NotFoundPage() {
  const navigate = useNavigate();

  return (
    <Result
      status="404"
      title="Không tìm thấy trang"
      subTitle="Đường dẫn bạn mở không tồn tại hoặc đã bị đổi."
      extra={
        <Button type="primary" onClick={() => navigate('/')}>
          Về trang chủ
        </Button>
      }
    />
  );
}
