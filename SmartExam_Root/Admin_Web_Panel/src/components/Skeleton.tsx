type SkeletonProps = {
  width?: string
  height?: string
  borderRadius?: string
  className?: string
}

export function Skeleton({ width = '100%', height = '1rem', borderRadius = '8px', className }: SkeletonProps) {
  return (
    <div
      className={`skeleton ${className ?? ''}`}
      style={{ width, height, borderRadius }}
      aria-hidden="true"
    />
  )
}

export function SkeletonStatCard() {
  return (
    <div className="card stat-card" style={{ gap: '1.25rem' }}>
      <Skeleton width="56px" height="56px" borderRadius="16px" />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
        <Skeleton width="60%" height="0.8rem" />
        <Skeleton width="40%" height="1.6rem" />
      </div>
    </div>
  )
}

export function SkeletonTableRow({ cols = 5 }: { cols?: number }) {
  return (
    <tr>
      {Array.from({ length: cols }).map((_, i) => (
        <td key={i} style={{ padding: '0.9rem 0.5rem' }}>
          <Skeleton height="0.85rem" width={i === 0 ? '70%' : '55%'} />
        </td>
      ))}
    </tr>
  )
}

export function SkeletonCard() {
  return (
    <div className="glass-card" style={{ padding: '1.5rem', display: 'flex', flexDirection: 'column', gap: '1rem' }}>
      <Skeleton width="40%" height="1rem" />
      <Skeleton width="80%" height="0.8rem" />
      <Skeleton width="60%" height="0.8rem" />
      <div style={{ display: 'flex', gap: '0.5rem', marginTop: '0.5rem' }}>
        <Skeleton width="80px" height="2rem" borderRadius="8px" />
        <Skeleton width="80px" height="2rem" borderRadius="8px" />
      </div>
    </div>
  )
}
