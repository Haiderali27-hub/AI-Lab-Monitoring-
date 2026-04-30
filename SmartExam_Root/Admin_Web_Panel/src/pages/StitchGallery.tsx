import { Link } from 'react-router-dom'
import { stitchScreens } from '../stitchScreens'

export function StitchGallery() {
  return (
    <main className="stitch-shell">
      <header className="stitch-hero">
        <div>
          <p className="eyebrow">SmartExam UI</p>
          <h1>Stitch Design Gallery</h1>
          <p>All screens are pulled directly from Stitch and embedded below.</p>
        </div>
        <div className="stitch-cta">
          <span>Total Screens</span>
          <strong>{stitchScreens.length}</strong>
        </div>
      </header>

      <section className="stitch-grid">
        {stitchScreens.map((screen) => (
          <article key={screen.slug} className="stitch-card">
            <div className="stitch-card-head">
              <h3>{screen.title}</h3>
              <Link to={`/ui/${screen.slug}`}>Open</Link>
            </div>
            <iframe title={screen.title} src={screen.url} loading="lazy" />
          </article>
        ))}
      </section>
    </main>
  )
}