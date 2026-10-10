
import { request } from './client';

const TOKEN_KEY = 'support-desk-token';
const USER_KEY = 'support-desk-user';

export interface AuthenticatedAgent {
  id: string;
  fullName: string;
  email: string;
}

export interface LoginResponse {
  token: string;
  expiresAtUtc: string;
  user: AuthenticatedAgent;
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function getStoredUser(): AuthenticatedAgent | null {
  const stored = localStorage.getItem(USER_KEY);

  if (!stored) return null;

  try {
    return JSON.parse(stored) as AuthenticatedAgent;
  } catch {
    clearSession();
    return null;
  }
}

export function saveSession(response: LoginResponse): void {
  localStorage.setItem(TOKEN_KEY, response.token);
  localStorage.setItem(USER_KEY, JSON.stringify(response.user));
}

export function clearSession(): void {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

export function login(email: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  });
}

export function getCurrentUser(): Promise<AuthenticatedAgent> {
  return request<AuthenticatedAgent>('/auth/me');
}
