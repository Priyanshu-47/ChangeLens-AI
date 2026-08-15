import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ProjectProvider, useProjects } from './ProjectContext';
import { installFetchMock } from '../test/helpers';

const projectA = { id: 'p1', name: 'AcmePay', slug: 'acmepay', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Engineer' };
const projectB = { id: 'p2', name: 'Northwind', slug: 'northwind', description: null, createdAtUtc: '2026-01-01T00:00:00Z', updatedAtUtc: null, memberRole: 'Viewer' };

function Harness() {
  const { projects, selected, selectProject } = useProjects();
  return (
    <div>
      <span data-testid="count">{projects.length}</span>
      <span data-testid="selected">{selected?.name ?? 'none'}</span>
      <button type="button" onClick={() => selectProject('p2')}>Switch</button>
    </div>
  );
}

describe('ProjectContext', () => {
  it('loads projects and defaults to the first one', async () => {
    installFetchMock([{ path: '/api/v1/projects', body: { items: [projectA, projectB], page: 1, pageSize: 50, total: 2 } }]);
    render(
      <ProjectProvider>
        <Harness />
      </ProjectProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('selected')).toHaveTextContent('AcmePay'));
    expect(screen.getByTestId('count')).toHaveTextContent('2');
  });

  it('switching persists the selection and updates the context', async () => {
    const user = userEvent.setup();
    installFetchMock([{ path: '/api/v1/projects', body: { items: [projectA, projectB], page: 1, pageSize: 50, total: 2 } }]);
    render(
      <ProjectProvider>
        <Harness />
      </ProjectProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('selected')).toHaveTextContent('AcmePay'));
    await user.click(screen.getByRole('button', { name: 'Switch' }));
    expect(screen.getByTestId('selected')).toHaveTextContent('Northwind');
    expect(window.localStorage.getItem('changelens.selectedProjectId')).toBe('p2');
  });
});
