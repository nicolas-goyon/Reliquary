/**
 * Reads the Prometheus metrics of the API replicas. E2E_METRICS_URLS gives the address of /metrics of each replica, separated by commas.
 * The API gives /metrics only on its own port, not through the web container.
 */
export const metricsUrls = (process.env.E2E_METRICS_URLS ?? '')
  .split(',')
  .map((url) => url.trim())
  .filter(Boolean)

/** Gives the sum of the samples of a metric, for all its attributes. A metric that is not in the output gives 0. */
export function sumOf(text: string, metric: string): number {
  let sum = 0
  for (const line of text.split('\n')) {
    if (line.startsWith('#')) continue
    const match = /^([a-zA-Z_:][a-zA-Z0-9_:]*)(\{[^}]*\})?\s+(\S+)/.exec(line)
    if (match && match[1] === metric) sum += Number(match[3])
  }
  return sum
}

/** Gives the lines of a metric, with their attributes. */
export function linesOf(text: string, metric: string): string[] {
  return text.split('\n').filter((line) => line.startsWith(`${metric}{`) || line.startsWith(`${metric} `))
}

export async function scrape(url: string): Promise<string> {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`${url} gave the status ${response.status}.`)
  return response.text()
}

/** The open WebSocket connections of the table hub on a replica. */
export async function hubConnections(url: string): Promise<number> {
  return sumOf(await scrape(url), 'signalr_server_active_connections')
}
