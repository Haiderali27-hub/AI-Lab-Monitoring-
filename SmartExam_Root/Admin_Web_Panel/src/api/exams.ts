import type { LiveRosterItem } from '../types'
import { apiClient, unwrap } from './client'

export async function getLiveRoster(): Promise<LiveRosterItem[]> {
  return unwrap<LiveRosterItem[]>(apiClient.get('/api/exams/admin/live-roster'))
}