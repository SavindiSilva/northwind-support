import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getToken, saveSession } from './auth';
import { request } from './client';

describe('API authentication', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    localStorage.clear();
  });

  it('attaches the saved bearer token to protected requests', async () => {
    saveSession({
      token: 'test-jwt-token',
      expiresAtUtc: '2026-10-11T12:00:00Z',
      user: { id: '1', fullName: 'Alex Turner', email: 'alex@example.com' },
    });

    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ items: [] }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await request('/tickets');

    expect(fetch).toHaveBeenCalledWith(
      '/api/tickets',
      expect.objectContaining({
        headers: expect.any(Headers),
      }),
    );

    const options = vi.mocked(fetch).mock.calls[0][1];
    expect(new Headers(options?.headers).get('Authorization'))
      .toBe('Bearer test-jwt-token');
  });

  it('clears the session and emits an event on a protected 401', async () => {
    saveSession({
      token: 'expired-jwt-token',
      expiresAtUtc: '2026-10-11T12:00:00Z',
      user: { id: '1', fullName: 'Alex Turner', email: 'alex@example.com' },
    });

    const unauthorized = vi.fn();
    window.addEventListener('auth:unauthorized', unauthorized);

    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ title: 'Unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await expect(request('/tickets')).rejects.toMatchObject({ status: 401 });

    expect(getToken()).toBeNull();
    expect(unauthorized).toHaveBeenCalledTimes(1);

    window.removeEventListener('auth:unauthorized', unauthorized);
  });

  it('does not emit the session-expired event for a login 401', async () => {
    const unauthorized = vi.fn();
    window.addEventListener('auth:unauthorized', unauthorized);

    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ title: 'Unauthorized' }), {
        status: 401,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    await expect(request('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email: 'wrong@example.com', password: 'wrong' }),
    })).rejects.toMatchObject({ status: 401 });

    expect(unauthorized).not.toHaveBeenCalled();
    window.removeEventListener('auth:unauthorized', unauthorized);
  });
});