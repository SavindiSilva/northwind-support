import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SlaCell } from './SlaCell';

describe('SlaCell', () => {
  it.each([
    ['WithinSla', 'Within SLA'],
    ['AtRisk', 'At risk'],
    ['Breached', 'Breached'],
    ['Met', 'Met'],
    ['NotApplicable', 'No SLA'],
  ] as const)('displays the %s state as readable text', (slaStatus, label) => {
    render(<SlaCell dueAtUtc={null} slaStatus={slaStatus} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });
});