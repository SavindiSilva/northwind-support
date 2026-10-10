import { useCallback, useEffect, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type {
  PagedResult,
  TicketFilters,
  TicketListItem,
} from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  slaStatus: undefined,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  isLoading: boolean;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const [state, setState] = useState<State>({ isLoading: true });
  const [debouncedSearch, setDebouncedSearch] = useState(filters.search);

  const load = useCallback(async (queryFilters: TicketFilters) => {
    setState((current) => ({
      ...current,
      isLoading: true,
      error: undefined,
    }));

    try {
      const data = await ticketsApi.getTickets(queryFilters);
      setState({ isLoading: false, data, error: undefined });
    } catch (error) {
      const message =
        error instanceof ApiError
          ? error.message
          : 'Unable to load tickets.';

      setState((current) => ({
        ...current,
        isLoading: false,
        error: message,
      }));
    }
  }, []);

  // Debounce search so typing doesn't trigger a request for every character.
  useEffect(() => {
    const handle = window.setTimeout(() => {
      setDebouncedSearch(filters.search);
    }, DEBOUNCE_MS);

    return () => window.clearTimeout(handle);
  }, [filters.search]);

  // Apply filters whenever a relevant filter or the debounced search changes.
  useEffect(() => {
    const queryFilters: TicketFilters = {
      search: debouncedSearch,
      status: filters.status,
      priority: filters.priority,
      categoryId: filters.categoryId,
      customerId: filters.customerId,
      assignedAgentId: filters.assignedAgentId,
      unassignedOnly: filters.unassignedOnly,
      slaStatus: filters.slaStatus,
      page: filters.page,
      pageSize: filters.pageSize,
      sortBy: filters.sortBy,
      sortDirection: filters.sortDirection,
    };

    void Promise.resolve().then(() => load(queryFilters));
  }, [
    load,
    debouncedSearch,
    filters.status,
    filters.priority,
    filters.categoryId,
    filters.customerId,
    filters.assignedAgentId,
    filters.unassignedOnly,
    filters.slaStatus,
    filters.page,
    filters.pageSize,
    filters.sortBy,
    filters.sortDirection,
  ]);

  const updateFilters = (patch: Partial<TicketFilters>) =>
    setFilters((current) => ({
      ...current,
      ...patch,
      page: patch.page ?? 1,
    }));

  const resetFilters = () => {
    setFilters(emptyFilters);
  };

  const reload = () =>
    load({
      search: debouncedSearch,
      status: filters.status,
      priority: filters.priority,
      categoryId: filters.categoryId,
      customerId: filters.customerId,
      assignedAgentId: filters.assignedAgentId,
      unassignedOnly: filters.unassignedOnly,
      slaStatus: filters.slaStatus,
      page: filters.page,
      pageSize: filters.pageSize,
      sortBy: filters.sortBy,
      sortDirection: filters.sortDirection,
    });

  return {
    filters,
    updateFilters,
    resetFilters,
    reload,
    ...state,
  };
}