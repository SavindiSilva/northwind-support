import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { TicketDetail, TicketEscalation } from '../../../types/api';
import { TicketEscalationPanel } from './TicketEscalationPanel';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getEscalations: vi.fn(),
    escalateTicket: vi.fn(),
  },
}));

const ticket: TicketDetail = {
  id: 12,
  reference: 'TCK-0012',
  title: 'Invoice total does not match the order',
  description: 'The invoice total is incorrect.',
  status: 'InProgress',
  priority: 'High',
  customer: {
    id: 2,
    name: 'Fabrikam Inc',
    tier: 'Standard',
    email: 'billing@example.com',
    phone: null,
  },
  category: { id: 2, name: 'Billing', requiresSpecialist: false },
  assignedAgent: { id: 4, fullName: 'Sara Lindqvist' },
  createdAtUtc: '2026-09-24T08:12:00Z',
  updatedAtUtc: '2026-09-25T10:03:00Z',
  dueAtUtc: '2026-09-24T16:12:00Z',
  resolvedAtUtc: null,
  slaStatus: 'Breached',
  triageReasons: [],
};

const escalation: TicketEscalation = {
  id: 1,
  ticketId: 12,
  fromPriority: 'High',
  toPriority: 'Critical',
  fromAgent: { id: 4, fullName: 'Sara Lindqvist' },
  toAgent: { id: 4, fullName: 'Sara Lindqvist' },
  fromDueAtUtc: '2026-09-24T16:12:00Z',
  toDueAtUtc: '2026-09-25T14:03:00Z',
  reason: 'Customer impact is increasing.',
  escalatedBy: 'Test User',
  escalatedAtUtc: '2026-09-25T10:03:00Z',
};

describe('TicketEscalationPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(ticketsApi.getEscalations).mockResolvedValue([]);
  });

  it('validates the escalation reason before submitting', async () => {
    render(<TicketEscalationPanel ticket={ticket} onUpdated={vi.fn()} />);


    fireEvent.change(screen.getByLabelText('Reason'), {
      target: { value: 'Bad' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(
      await screen.findByText('The reason must contain between 5 and 500 characters.'),
    ).toBeInTheDocument();
    expect(ticketsApi.escalateTicket).not.toHaveBeenCalled();
  });

  it('submits a valid escalation and refreshes the ticket and history', async () => {
    const onUpdated = vi.fn();
    const updatedTicket = { ...ticket, priority: 'Critical' as const };

    vi.mocked(ticketsApi.escalateTicket).mockResolvedValue({
      ticket: updatedTicket,
      escalation,
    });
    vi.mocked(ticketsApi.getEscalations).mockResolvedValueOnce([]).mockResolvedValueOnce([
      escalation,
    ]);

    render(<TicketEscalationPanel ticket={ticket} onUpdated={onUpdated} />);


    fireEvent.change(screen.getByLabelText('Reason'), {
      target: { value: 'Customer impact is increasing.' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    await waitFor(() => {
      expect(ticketsApi.escalateTicket).toHaveBeenCalledWith(12, {
        reason: 'Customer impact is increasing.',
      });
    });

    expect(onUpdated).toHaveBeenCalledWith(updatedTicket);
    expect(await screen.findByText('Customer impact is increasing.')).toBeInTheDocument();
  });

  it('shows a conflict returned by the API', async () => {
    vi.mocked(ticketsApi.escalateTicket).mockRejectedValue(
      new ApiError(409, 'This ticket can no longer be escalated.'),
    );

    const onUpdated = vi.fn();
    render(<TicketEscalationPanel ticket={ticket} onUpdated={onUpdated} />);


    fireEvent.change(screen.getByLabelText('Reason'), {
      target: { value: 'Customer impact is increasing.' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Escalate ticket' }));

    expect(
      await screen.findByText('This ticket can no longer be escalated.'),
    ).toBeInTheDocument();

    expect(onUpdated).not.toHaveBeenCalled();
  });

  it('does not show the form for a critical ticket', async () => {
    render(
      <TicketEscalationPanel
        ticket={{ ...ticket, priority: 'Critical' }}
        onUpdated={vi.fn()}
      />,
    );
    await screen.findByText('No escalations recorded.');

    expect(screen.queryByRole('button', { name: 'Escalate ticket' })).not.toBeInTheDocument();
    expect(
      screen.getByText('This ticket cannot be escalated in its current state.'),
    ).toBeInTheDocument();
  });
});