import type { IncidentEvent } from '../api/types';

function formatTime(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) {
    return iso;
  }
  return d.toISOString();
}

/**
 * Chronological incident timeline (actual data only — timestamps are the incident
 * events' occurredAtUtc values; nothing is fabricated).
 */
export function Timeline({ events }: { events: IncidentEvent[] }) {
  const ordered = [...events].sort(
    (a, b) => new Date(a.occurredAtUtc).getTime() - new Date(b.occurredAtUtc).getTime(),
  );

  if (ordered.length === 0) {
    return <p className="muted small">No timeline events recorded for this incident.</p>;
  }

  return (
    <ol className="timeline">
      {ordered.map((event) => (
        <li key={event.id} className="timeline-item" data-type={event.type}>
          <div className="timeline-time">{formatTime(event.occurredAtUtc)}</div>
          <div className="timeline-type">{event.type}</div>
          {event.message ? <p className="timeline-message">{event.message}</p> : null}
          {event.source ? <div className="timeline-source">{event.source}</div> : null}
        </li>
      ))}
    </ol>
  );
}
