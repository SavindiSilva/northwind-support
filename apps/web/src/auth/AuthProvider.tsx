
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import {
  clearSession,
  getCurrentUser,
  getStoredUser,
  login as loginRequest,
  saveSession,
  type AuthenticatedAgent,
} from '../api/auth';

interface AuthContextValue {
  user: AuthenticatedAgent | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthenticatedAgent | null>(() => getStoredUser());
  const [loading, setLoading] = useState(true);

  const logout = useCallback(() => {
    clearSession();
    setUser(null);
  }, []);

  const login = useCallback(async (email: string, password: string) => {
    const response = await loginRequest(email, password);
    saveSession(response);
    setUser(response.user);
  }, []);

  useEffect(() => {
    let active = true;

    const handleUnauthorized = () => {
      if (active) {
        setUser(null);
      }
    };

    window.addEventListener('auth:unauthorized', handleUnauthorized);

    const token = localStorage.getItem('support-desk-token');

    if (token) {
      getCurrentUser()
        .then((currentUser) => {
          if (active) {
            setUser(currentUser);
          }
        })
        .catch(() => {
          if (active) {
            clearSession();
            setUser(null);
          }
        })
        .finally(() => {
          if (active) {
            setLoading(false);
          }
        });
    } else {
      queueMicrotask(() => {
        if (active) {
          setLoading(false);
        }
      });
    }

    return () => {
      active = false;
      window.removeEventListener('auth:unauthorized', handleUnauthorized);
    };
  }, []);

  const value = useMemo(
    () => ({ user, loading, login, logout }),
    [user, loading, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);

  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider.');
  }

  return context;
}