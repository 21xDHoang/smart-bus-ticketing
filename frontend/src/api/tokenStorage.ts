// Nơi DUY NHẤT đọc/ghi cặp token vào localStorage.
//
// Trước đây App.tsx, axiosClient.ts và LoginForm.tsx mỗi nơi tự gọi localStorage
// với tên key viết tay — chỉ cần một chỗ gõ sai là token "biến mất" mà không ai biết.
// Gom về đây để đổi tên key hay đổi nơi lưu chỉ phải sửa một file.

const ACCESS_TOKEN_KEY = 'access_token';
const REFRESH_TOKEN_KEY = 'refresh_token';

export const tokenStorage = {
  getAccessToken(): string | null {
    return localStorage.getItem(ACCESS_TOKEN_KEY);
  },

  getRefreshToken(): string | null {
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  },

  /** Lưu cặp token mới. Gọi sau khi login hoặc sau khi refresh thành công. */
  save(accessToken: string, refreshToken: string): void {
    localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
  },

  /** Xoá sạch phiên phía client. Gọi khi đăng xuất hoặc khi refresh token hỏng. */
  clear(): void {
    localStorage.removeItem(ACCESS_TOKEN_KEY);
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  },
};

export default tokenStorage;
