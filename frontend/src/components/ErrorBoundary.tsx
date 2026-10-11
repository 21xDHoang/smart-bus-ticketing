import { Component } from 'react';
import type { ErrorInfo, ReactNode } from 'react';
import { Button, Result } from 'antd';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

/**
 * Lưới an toàn cho phần nội dung: một màn hình lỗi không kéo sập cả khung điều hướng —
 * header/menu vẫn dùng được. AppLayout gắn `key` theo đường dẫn nên chỉ cần sang màn khác
 * là thoát khỏi trạng thái lỗi.
 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Ghi lại để còn manh mối khi người dùng báo lỗi — chỉ vào console, không gửi đi đâu.
    console.error('Màn hình gặp lỗi không mong đợi:', error, info.componentStack);
  }

  private reload = () => window.location.reload();

  render() {
    if (this.state.error === null) return this.props.children;

    return (
      <Result
        status="error"
        title="Màn hình này đang gặp lỗi"
        subTitle="Thử tải lại trang. Nếu vẫn lỗi, quay về trang chủ hoặc báo cho ban quản trị."
        extra={[
          <Button key="reload" type="primary" onClick={this.reload}>
            Tải lại trang
          </Button>,
          <Button key="home" href="/">
            Về trang chủ
          </Button>,
        ]}
      />
    );
  }
}
