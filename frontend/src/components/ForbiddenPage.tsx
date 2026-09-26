import { Button, Result } from 'antd';
import { useNavigate } from 'react-router-dom';

/**
 * Màn hình 403 — hiện khi người dùng ĐÃ đăng nhập nhưng không đủ quyền vào một màn hình.
 *
 * Cố ý KHÔNG đá về trang đăng nhập như lỗi 401: đăng nhập lại cũng không đổi được vai trò,
 * đá về chỉ khiến người dùng tưởng mất phiên rồi đăng nhập lại vô ích.
 *
 * Câu thông báo giữ nguyên văn câu backend trả về trong lỗi 403 (docs/api-contract.md) để
 * người dùng gặp đúng một câu, dù bị chặn ở client hay ở server.
 */
export default function ForbiddenPage() {
  const navigate = useNavigate();

  return (
    <Result
      status="403"
      title="403"
      subTitle="Bạn không có quyền truy cập tính năng này."
      extra={
        <Button type="primary" onClick={() => navigate('/')}>
          Về trang chủ
        </Button>
      }
    />
  );
}
