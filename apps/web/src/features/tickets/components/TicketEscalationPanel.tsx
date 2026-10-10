import { useCallback, useEffect, useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { formatDateTime } from '../../../lib/format';
import type { TicketDetail, TicketEscalation } from '../../../types/api';

interface TicketEscalationPanelProps {
  ticket: TicketDetail;
  onUpdated: (ticket: TicketDetail) => void;
}

export function TicketEscalationPanel({
  ticket,
  onUpdated,
}: TicketEscalationPanelProps) {
  const [reason, setReason] = useState('');
  const [escalatedBy, setEscalatedBy] = useState('');
  const [history, setHistory] = useState<TicketEscalation[]>([]);
  const [loadingHistory, setLoadingHistory] = useState(true);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [historyError, setHistoryError] = useState<string>();

  const loadHistory = useCallback(async () => {
    setLoadingHistory(true);
    setHistoryError(undefined);

    try {
      setHistory(await ticketsApi.getEscalations(ticket.id));
    } catch (caught) {
      setHistoryError(toErrorMessage(caught, 'Could not load escalation history.'));
    } finally {
      setLoadingHistory(false);
    }
  }, [ticket.id]);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  const terminal =
    ticket.priority === 'Critical' ||
    ticket.status === 'Resolved' ||
    ticket.status === 'Closed';

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    const trimmedReason = reason.trim();
    const trimmedActor = escalatedBy.trim();

    if (trimmedReason.length < 5 || trimmedReason.length > 500) {
      setError('The reason must contain between 5 and 500 characters.');
      return;
    }

    if (!trimmedActor) {
      setError('Enter who is requesting the escalation.');
      return;
    }

    setPending(true);
    setError(undefined);

    try {
      const result = await ticketsApi.escalateTicket(ticket.id, {
        reason: trimmedReason,
        escalatedBy: trimmedActor,
      });

      onUpdated(result.ticket);
      setReason('');
      await loadHistory();
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.status === 409
          ? caught.message || 'This ticket can no longer be escalated.'
          : toErrorMessage(caught, 'Could not escalate this ticket.'),
      );
    } finally {
      setPending(false);
    }
  };

  return (
    <section className="card">
      <h2>Escalation</h2>

      {terminal ? (
        <p>This ticket cannot be escalated in its current state.</p>
      ) : (
        <form onSubmit={(event) => void submit(event)}>
          <div className="field">
            <label htmlFor="escalation-reason">Reason</label>
            <textarea
              id="escalation-reason"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              minLength={5}
              maxLength={500}
              rows={3}
              required
              disabled={pending}
              aria-describedby="escalation-reason-help"
            />
            <small id="escalation-reason-help">
              Explain why this ticket needs a higher priority (5–500 characters).
            </small>
          </div>

          <div className="field">
            <label htmlFor="escalated-by">Requested by</label>
            <input
              id="escalated-by"
              value={escalatedBy}
              onChange={(event) => setEscalatedBy(event.target.value)}
              maxLength={100}
              required
              disabled={pending}
            />
          </div>

          <button
            type="submit"
            className="button button--primary"
            disabled={pending || !reason.trim() || !escalatedBy.trim()}
          >
            {pending ? 'Escalating...' : 'Escalate ticket'}
          </button>
        </form>
      )}

      {error && <p className="field__error" role="alert">{error}</p>}

      <h3>Escalation history</h3>

      {loadingHistory && <p>Loading escalation history...</p>}

      {historyError && (
        <div>
          <p className="field__error" role="alert">{historyError}</p>
          <button type="button" className="button button--ghost" onClick={() => void loadHistory()}>
            Retry history
          </button>
        </div>
      )}

      {!loadingHistory && !historyError && history.length === 0 && (
        <p>No escalations recorded.</p>
      )}

      {history.map((entry) => (
        <article key={entry.id} className="escalation-history__item">
          <p>
            <strong>{entry.fromPriority} → {entry.toPriority}</strong>
            {' · '}
            {formatDateTime(entry.escalatedAtUtc)}
          </p>
          <p>{entry.reason}</p>
          <p>
            {entry.fromAgent?.fullName ?? 'Unassigned'}
            {' → '}
            {entry.toAgent?.fullName ?? 'Unassigned'}
          </p>
          <small>Requested by {entry.escalatedBy}</small>
        </article>
      ))}
    </section>
  );
}
