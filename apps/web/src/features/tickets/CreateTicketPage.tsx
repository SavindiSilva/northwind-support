import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Spinner } from '../../components/Spinner';
import { useReferenceData } from '../../hooks/useReferenceData';
import type { TicketDetail } from '../../types/api';
import { CreateTicketForm } from './components/CreateTicketForm';

export function CreateTicketPage() {
  const reference = useReferenceData();
  const navigate = useNavigate();
  const [created, setCreated] = useState<TicketDetail>();

  return (
    <section>
      <header className="page-header">
        <div>
          <p className="breadcrumb">
            <Link to="/tickets">Tickets</Link> / New
          </p>
          <h1>Raise a ticket</h1>
        </div>
      </header>

      {reference.isLoading && <Spinner label="Loading customers and categories" />}

      {reference.error && (
        <ErrorMessage message={reference.error} onRetry={() => void reference.reload()} />
      )}

      {created && (
        <div className="card notice" role="status">
          <p>
            Raised <strong>{created.reference}</strong> for {created.customer.name}.
          </p>

          <p>
            <strong>Priority:</strong> {created.priority}
          </p>
          <p>
            <strong>Assigned to:</strong>{' '}
            {created.assignedAgent?.fullName ?? 'Unassigned'}
          </p>
          <p>
            <strong>SLA deadline:</strong>{' '}
            {created.dueAtUtc
              ? new Date(created.dueAtUtc).toLocaleString()
              : 'Not applicable'}
          </p>

          {created.triage && (
            <>
              <h3>Triage decisions</h3>
              <ul>
                <li>
                  <strong>Priority decision:</strong>{' '}
                  {created.triage.priorityReason}
                </li>
                <li>
                  <strong>SLA window:</strong>{' '}
                  {created.triage.slaWindowHours} hours
                </li>
                <li>
                  <strong>SLA explanation:</strong>{' '}
                  {created.triage.slaReason}
                </li>
                <li>
                  <strong>Assignment decision:</strong>{' '}
                  {created.triage.assignmentReason}
                </li>
              </ul>
            </>
          )}

          <div className="button-row">
            <button
              type="button"
              className="button button--primary"
              onClick={() => void navigate(`/tickets/${created.id}`)}
            >
              Open {created.reference}
            </button>
            <button
              type="button"
              className="button button--ghost"
              onClick={() => setCreated(undefined)}
            >
              Raise another
            </button>
          </div>
        </div>
      )}

      {reference.data && !created && (
        <CreateTicketForm
          customers={reference.data.customers}
          categories={reference.data.categories}
          onCreated={setCreated}
        />
      )}
    </section>
  );
}
