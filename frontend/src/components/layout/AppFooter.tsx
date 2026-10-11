import { Layout } from 'antd';

const { Footer } = Layout;

/** Dòng bản quyền cuối trang; ẩn trên điện thoại khi đã có thanh tab đáy. */
export default function AppFooter({ hidden = false }: { hidden?: boolean }) {
  if (hidden) return null;
  return (
    <Footer
      style={{ textAlign: 'center', color: 'var(--app-text-muted)', background: 'transparent' }}
    >
      Smart Bus Ticket ©2026 — Hệ thống đặt vé xe buýt
    </Footer>
  );
}
