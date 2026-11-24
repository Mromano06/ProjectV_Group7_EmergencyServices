export async function getApiData<T>(url: string): Promise<T> {
  const token = import.meta.env.VITE_API_TOKEN
  const response = await fetch(url, {
    headers: {
      Accept: 'application/json',
      ...(token
        ? { Authorization: `Bearer ${token}` }
        : {}),
    },
  })

  if (!response.ok) {
    throw new Error(`Request failed: ${response.status}`)
  }

  return response.json() as Promise<T>
}