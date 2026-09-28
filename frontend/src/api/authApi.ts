import axiosClient from './axiosClient';

// Tên claim JWT do backend C# phát hành (System.Security.Claims).
// Kèm cả tên rút gọn để phòng trường hợp JwtSecurityTokenHandler ánh xạ claim.
const CLAIM_NAME = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name';
const CLAIM_ROLE = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  tokenType: string;
  expiresIn: number;
}

export interface DecodedUser {
  id: string | null;
  fullName: string | null;
  role: string | null;
  phoneNumber: string | null;
}

export interface LoginPayload {
  phoneNumber: string;
  password: string;
}

export interface RegisterPayload {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
}

/**
 * Giải mã payload của Access Token (JWT) để lấy thông tin người dùng.
 * Backend chưa có endpoint /auth/profile nên thông tin (id, họ tên, vai trò, SĐT)
 * được đọc trực tiếp từ claims trong token.
 */
export function decodeAccessToken(token: string): DecodedUser | null {
  try {
    const payload = token.split('.')[1];

    // `atob` trả về chuỗi NHỊ PHÂN — mỗi ký tự là một byte, không phải văn bản Unicode.
    // Payload JWT là JSON mã hoá UTF-8, nên đưa thẳng chuỗi đó cho `JSON.parse` thì tên
    // tiếng Việt bị đọc nhầm theo Latin-1: "Phùng Duy Hoàng" thành "PhÃ¹ng Duy HoÃ ng".
    // Phải dựng lại mảng byte rồi giải mã UTF-8 bằng `TextDecoder`.
    const bytes = Uint8Array.from(
      atob(payload.replace(/-/g, '+').replace(/_/g, '/')),
      (char) => char.charCodeAt(0),
    );
    const decoded = JSON.parse(new TextDecoder().decode(bytes)) as Record<string, unknown>;

    return {
      id: (decoded.sub as string) ?? null,
      fullName:
        (decoded[CLAIM_NAME] as string) ?? (decoded.unique_name as string) ?? null,
      role:
        (decoded[CLAIM_ROLE] as string) ?? (decoded.role as string) ?? null,
      phoneNumber: (decoded.phoneNumber as string) ?? null,
    };
  } catch {
    return null;
  }
}

const authApi = {
  // POST /api/auth/login — body { phoneNumber, password } → { accessToken, refreshToken, … }
  login: (data: LoginPayload): Promise<AuthResponse> =>
    axiosClient.post<AuthResponse, AuthResponse>('/auth/login', data),

  // POST /api/auth/register — backend chưa có endpoint này (task của Hiếu, sẽ bổ sung sau).
  register: (data: RegisterPayload): Promise<AuthResponse> =>
    axiosClient.post<AuthResponse, AuthResponse>('/auth/register', data),

  // POST /api/auth/refresh-token — body { refreshToken } → cặp token mới.
  refreshToken: (refreshToken: string): Promise<AuthResponse> =>
    axiosClient.post<AuthResponse, AuthResponse>('/auth/refresh-token', { refreshToken }),

  // POST /api/auth/logout — body { refreshToken } → thu hồi refresh token (204 No Content).
  logout: (refreshToken: string): Promise<void> =>
    axiosClient.post<never, void>('/auth/logout', { refreshToken }),
};

export default authApi;
