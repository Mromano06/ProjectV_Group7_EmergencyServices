import { useEffect, useState } from 'react'
import './App.css'
import { getApiData } from './api/apiClient'

type WeatherWarning = {
  id: string
  type: string
  severity: string
  area: string
  description: string | null
  startsAt: string
  endsAt: string
  status: string
  source: string
  affectsTransit: boolean
}

function App() {
  const [warnings, setWarnings] = useState<WeatherWarning[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getApiData<WeatherWarning[]>(
      '/api/emergency/v1/weather-warnings?active=true',
    )
      .then(setWarnings)
      .catch((requestError: Error) => {
        setError(requestError.message)
      })
      .finally(() => {
        setLoading(false)
      })
  }, [])

  if (loading) {
    return <p className="message">Loading weather warnings...</p>
  }

  if (error) {
    return <p className="message error">Could not load warnings: {error}</p>
  }

  return (
    <main className="app">
      <h1>Emergency Services</h1>
      <h2>Active Weather Warnings</h2>

      {warnings.length === 0 ? (
        <p className="message">No active warnings.</p>
      ) : (
        <section className="warning-list">
          {warnings.map((warning) => (
            <article className="warning-card" key={warning.id}>
              <h3>{warning.type}</h3>

              <p>
                <strong>Severity:</strong> {warning.severity}
              </p>

              <p>
                <strong>Area:</strong> {warning.area}
              </p>

              <p>
                <strong>Status:</strong> {warning.status}
              </p>

              {warning.description && (
                <p>
                  <strong>Details:</strong> {warning.description}
                </p>
              )}

              <p>
                <strong>Ends:</strong>{' '}
                {new Date(warning.endsAt).toLocaleString()}
              </p>

              <p>
                <strong>Source:</strong> {warning.source}
              </p>

              {warning.affectsTransit && (
                <p className="transit-warning">
                  This warning affects transit.
                </p>
              )}
            </article>
          ))}
        </section>
      )}
    </main>
  )
}

export default App