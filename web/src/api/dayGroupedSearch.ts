export type SourceType = 'News' | 'Announcement'

export interface SearchItem {
  newsId: string
  title: string
  highlight: string | null
  publisher: string
  author: string
  sourceType: SourceType
  publishTime: string
}

export interface SearchDayGroup {
  date: string
  items: SearchItem[]
}

export interface DayGroupedSearchResponse {
  searchTraceId: string
  searchMode: string
  degraded: boolean
  degradationMode: string | null
  maxDepthReached: boolean
  page: number
  pageSize: number
  totalDays: number
  totalPages: number
  totalItems: number
  newsItems: number
  announcementItems: number
  days: SearchDayGroup[]
}

export interface DayGroupedSearchRequest {
  query: string
  sourceTypes: SourceType[]
  page: number
  pageSize: number
}

export async function searchByDay(
  endpoint: string,
  request: DayGroupedSearchRequest,
  signal?: AbortSignal,
): Promise<DayGroupedSearchResponse> {
  const response = await fetch(endpoint, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
    signal,
  })
  if (!response.ok) {
    throw new Error(response.status === 503 ? '检索服务暂时不可用' : `检索失败（${response.status}）`)
  }
  return response.json() as Promise<DayGroupedSearchResponse>
}
