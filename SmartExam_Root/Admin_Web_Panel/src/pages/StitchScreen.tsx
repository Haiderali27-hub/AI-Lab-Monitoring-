import { Link, useParams } from 'react-router-dom'
import { stitchScreens } from '../stitchScreens'

export function StitchScreen() {
  const { slug } = useParams()
  const screen = stitchScreens.find((item) => item.slug === slug)

  if (!screen) {
    return (
      <main className="stitch-shell">
        <header className="stitch-hero">
          <div>
            <p className="eyebrow">SmartExam UI</p>
            <h1>Screen Not Found</h1>
            <p>Return to the gallery and choose another screen.</p>
          </div>
          <Link to="/ui" className="stitch-link">Back to Gallery</Link>
        </header>
      </main>
    )
  }

  return (
    <main className="stitch-shell">
      <header className="stitch-hero">
        <div>
          <p className="eyebrow">SmartExam UI</p>
          <h1>{screen.title}</h1>
          <p>Embedded directly from Stitch.</p>
        </div>
        <Link to="/ui" className="stitch-link">Back to Gallery</Link>
      </header>

      <section className="stitch-single">
        <iframe title={screen.title} src={screen.url} />
      </section>
    </main>
  )
}