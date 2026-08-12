<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  searchByDay,
  type DayGroupedSearchResponse,
  type SearchItem,
  type SourceType,
} from '../api/dayGroupedSearch'

type FilterType = 'all' | 'news' | 'announcement'
interface TextSegment { text: string; highlighted: boolean }

const props = withDefaults(defineProps<{
  endpoint?: string
  daysPerPage?: number
  title?: string
  subtitle?: string
  aiUrl?: string
}>(), {
  endpoint: '/api/v1/search/day-groups',
  daysPerPage: 5,
  title: '动态与公告',
  subtitle: '掌握最新资讯，查收重要通知',
  aiUrl: '',
})

const query = ref('')
const filter = ref<FilterType>('all')
const startDate = ref('')
const endDate = ref('')
const page = ref(1)
const result = ref<DayGroupedSearchResponse | null>(null)
const loading = ref(false)
const error = ref('')
const aiMode = ref(false)
const expanded = ref(new Set<string>())
let debounceTimer: ReturnType<typeof setTimeout> | undefined
let requestController: AbortController | undefined

const sourceTypes = computed<SourceType[]>(() => {
  if (filter.value === 'news') return ['News']
  if (filter.value === 'announcement') return ['Announcement']
  return ['News', 'Announcement']
})

const pageNumbers = computed<(number | 'ellipsis')[]>(() => {
  const total = result.value?.totalPages ?? 0
  if (total <= 7) return Array.from({ length: total }, (_, index) => index + 1)
  const pages: (number | 'ellipsis')[] = [1]
  if (page.value > 3) pages.push('ellipsis')
  for (let value = Math.max(2, page.value - 1); value <= Math.min(total - 1, page.value + 1); value += 1) {
    pages.push(value)
  }
  if (page.value < total - 2) pages.push('ellipsis')
  pages.push(total)
  return pages
})

async function load() {
  requestController?.abort()
  requestController = new AbortController()
  loading.value = true
  error.value = ''
  try {
    result.value = await searchByDay(props.endpoint, {
      query: query.value.trim(),
      sourceTypes: sourceTypes.value,
      publishTimeFrom: startDate.value ? `${startDate.value}T00:00:00+08:00` : undefined,
      publishTimeTo: endDate.value ? `${endDate.value}T23:59:59.999+08:00` : undefined,
      page: page.value,
      pageSize: props.daysPerPage,
    }, requestController.signal)
  } catch (reason) {
    if (reason instanceof DOMException && reason.name === 'AbortError') return
    error.value = reason instanceof Error ? reason.message : '检索失败，请稍后重试'
  } finally {
    if (!requestController?.signal.aborted) loading.value = false
  }
}

function scheduleSearch() {
  clearTimeout(debounceTimer)
  debounceTimer = setTimeout(() => {
    page.value = 1
    void load()
  }, 300)
}

function selectFilter(value: FilterType) {
  if (filter.value === value) return
  filter.value = value
  page.value = 1
  void load()
}

function applyDateFilter() {
  page.value = 1
  void load()
}

function selectPage(value: number) {
  if (value === page.value || value < 1 || value > (result.value?.totalPages ?? 0)) return
  page.value = value
  expanded.value = new Set()
  void load()
  document.querySelector('.news-search')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}

function toggleItem(newsId: string) {
  const next = new Set(expanded.value)
  next.has(newsId) ? next.delete(newsId) : next.add(newsId)
  expanded.value = next
}

function plainText(value: string | null) {
  return (value ?? '').replace(/<[^>]*>/g, '').replace(/\s+/g, ' ').trim()
}

function highlightSegments(value: string | null): TextSegment[] {
  const text = plainText(value)
  const keyword = query.value.trim()
  if (!keyword) return [{ text, highlighted: false }]
  const escaped = keyword.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
  const matcher = new RegExp(`(${escaped})`, 'gi')
  return text.split(matcher).filter(Boolean).map(part => ({
    text: part,
    highlighted: part.toLocaleLowerCase() === keyword.toLocaleLowerCase(),
  }))
}

function formatTime(iso: string) {
  return new Intl.DateTimeFormat('zh-CN', {
    timeZone: 'Asia/Shanghai', hour: '2-digit', minute: '2-digit', hourCycle: 'h23',
  }).format(new Date(iso))
}

function todayKey() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Shanghai', year: 'numeric', month: '2-digit', day: '2-digit',
  }).formatToParts(new Date())
  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find(item => item.type === type)?.value ?? ''
  return `${part('year')}-${part('month')}-${part('day')}`
}

function formatDate(date: string) {
  const day = new Date(`${date}T00:00:00+08:00`)
  const today = new Date(`${todayKey()}T00:00:00+08:00`)
  const difference = Math.round((today.getTime() - day.getTime()) / 86_400_000)
  const relative = difference === 0 ? '今天' : difference === 1 ? '昨天' : difference === 2 ? '前天' : `${day.getMonth() + 1}月${day.getDate()}日`
  const weekday = new Intl.DateTimeFormat('zh-CN', { weekday: 'long', timeZone: 'Asia/Shanghai' }).format(day)
  return { relative, detail: `${day.getFullYear()}年${day.getMonth() + 1}月${day.getDate()}日 ${weekday}` }
}

function isExpandable(item: SearchItem) {
  return plainText(item.summary).length > 80
}

watch(query, scheduleSearch)
onMounted(load)
onBeforeUnmount(() => {
  clearTimeout(debounceTimer)
  requestController?.abort()
})
</script>

<template>
  <section class="news-search" aria-label="新闻公告检索">
    <header class="component-header">
      <h1>{{ title }}</h1>
      <p>{{ subtitle }}</p>
    </header>

    <div class="toolbar">
      <div class="primary-controls">
        <label class="search-box">
          <svg viewBox="0 0 20 20" aria-hidden="true"><circle cx="8.5" cy="8.5" r="5.75"/><path d="m13 13 4 4"/></svg>
          <span class="sr-only">搜索新闻与公告</span>
          <input v-model="query" type="search" placeholder="搜索动态、正文或发布者..." />
        </label>
        <div class="date-range">
          <label><span class="sr-only">开始日期</span><input v-model="startDate" type="date" :max="endDate || undefined" @change="applyDateFilter" /></label>
          <span aria-hidden="true">-</span>
          <label><span class="sr-only">结束日期</span><input v-model="endDate" type="date" :min="startDate || undefined" @change="applyDateFilter" /></label>
        </div>
      </div>
      <div class="secondary-controls">
        <div class="segments" aria-label="内容类型">
          <button v-for="option in ([['all', '全部'], ['news', '新闻'], ['announcement', '公告']] as const)"
            :key="option[0]" type="button" :class="{ active: filter === option[0] }"
            @click="selectFilter(option[0])">{{ option[1] }}</button>
        </div>
        <button type="button" class="ai-button" :class="{ active: aiMode }" @click="aiMode = !aiMode">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09l2.846.813-2.846.813a4.5 4.5 0 0 0-3.09 3.09ZM18.259 8.715 18 9.75l-.259-1.035a3.375 3.375 0 0 0-2.455-2.456L14.25 6l1.036-.259a3.375 3.375 0 0 0 2.455-2.456L18 2.25l.259 1.035a3.375 3.375 0 0 0 2.456 2.456L21.75 6l-1.035.259a3.375 3.375 0 0 0-2.456 2.456ZM16.894 20.567 16.5 21.75l-.394-1.183a2.25 2.25 0 0 0-1.423-1.423L13.5 18.75l1.183-.394a2.25 2.25 0 0 0 1.423-1.423l.394-1.183.394 1.183a2.25 2.25 0 0 0 1.423 1.423l1.183.394-1.183.394a2.25 2.25 0 0 0-1.423 1.423Z"/></svg>
          AI 检索
        </button>
      </div>
    </div>

    <div v-if="aiMode" class="ai-panel">
      <iframe v-if="aiUrl" :src="aiUrl" title="AI 智能检索" />
      <div v-else class="ai-placeholder">
        <div class="placeholder-border" aria-hidden="true" />
        <div class="placeholder-content">
          <div class="placeholder-icon">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9.813 15.904 9 18.75l-.813-2.846a4.5 4.5 0 0 0-3.09-3.09L2.25 12l2.846-.813a4.5 4.5 0 0 0 3.09-3.09L9 5.25l.813 2.846a4.5 4.5 0 0 0 3.09 3.09l2.846.813-2.846.813a4.5 4.5 0 0 0-3.09 3.09Z"/></svg>
          </div>
          <h2>AI 智能问答与检索</h2>
          <p>已开启 RAG 检索模式。<br>此区域为 <code>&lt;iframe&gt;</code> 占位符，请在生产环境中替换为真实的大模型交互界面。</p>
        </div>
      </div>
    </div>

    <template v-else>
      <div v-if="result" class="summary" aria-live="polite">
        <span v-if="query">搜索「<strong>{{ query }}</strong>」共找到 {{ result.totalItems }} 条记录</span>
        <span v-else>共收录 {{ result.newsItems }} 条新闻与 {{ result.announcementItems }} 条公告</span>
        <span v-if="result.degraded" class="degraded">当前为降级检索</span>
      </div>

      <div v-if="loading && !result" class="state-card">正在检索…</div>
      <div v-else-if="error" class="state-card error-state">
        <p>{{ error }}</p><button type="button" @click="load">重新加载</button>
      </div>
      <div v-else-if="result?.days.length === 0" class="state-card empty-state">
        <svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/></svg>
        <strong>未找到相关结果</strong><span>尝试其他关键词或更改筛选条件</span>
      </div>

      <div v-else class="day-list" :class="{ refreshing: loading }">
        <section v-for="group in result?.days" :key="group.date" class="day-section">
          <header class="day-header">
            <h2>{{ formatDate(group.date).relative }}</h2><span>{{ formatDate(group.date).detail }}</span>
          </header>
          <div class="day-card">
            <article v-for="item in group.items" :key="item.newsId" class="result-row">
              <div class="item-meta">
                <time :datetime="item.publishTime">{{ formatTime(item.publishTime) }}</time>
                <span class="type-badge" :class="item.sourceType.toLowerCase()">{{ item.sourceType === 'Announcement' ? '公告' : '新闻' }}</span>
              </div>
              <div class="item-body">
                <button type="button" class="item-title" @click="toggleItem(item.newsId)">
                  <template v-for="(segment, index) in highlightSegments(item.title)" :key="index"><mark v-if="segment.highlighted">{{ segment.text }}</mark><template v-else>{{ segment.text }}</template></template>
                </button>
                <div class="byline"><strong>{{ item.publisher }}</strong><template v-if="item.author"><i>·</i><span>{{ item.author }}</span></template></div>
                <template v-if="item.sourceType === 'Announcement'">
                  <div class="announcement-html" :class="{ collapsed: !expanded.has(item.newsId) }" v-html="item.contentHtml ?? ''" />
                </template>
                <p v-else class="snippet" :class="{ collapsed: !expanded.has(item.newsId) }">
                  <template v-for="(segment, index) in highlightSegments(item.summary)" :key="index"><mark v-if="segment.highlighted">{{ segment.text }}</mark><template v-else>{{ segment.text }}</template></template>
                </p>
                <button v-if="item.sourceType === 'Announcement' || isExpandable(item)" type="button" class="expand-button" @click="toggleItem(item.newsId)">
                  {{ expanded.has(item.newsId) ? item.sourceType === 'Announcement' ? '收起全文' : '收起' : item.sourceType === 'Announcement' ? '阅读全文' : '展开' }}
                  <svg viewBox="0 0 12 12" :class="{ rotated: expanded.has(item.newsId) }" aria-hidden="true"><path d="m2 4 4 4 4-4"/></svg>
                </button>
              </div>
              <div v-if="item.sourceType === 'News' && item.cover" class="cover-image">
                <img :src="item.cover" alt="" loading="lazy" />
              </div>
            </article>
          </div>
        </section>
      </div>

      <nav v-if="(result?.totalPages ?? 0) > 0" class="pagination" aria-label="检索结果分页">
        <button type="button" class="page-arrow" :disabled="page === 1" aria-label="上一页" @click="selectPage(page - 1)"><svg viewBox="0 0 20 20"><path d="m12.5 15-5-5 5-5"/></svg></button>
        <div class="page-numbers">
          <template v-for="(number, index) in pageNumbers" :key="`${number}-${index}`">
            <span v-if="number === 'ellipsis'">…</span>
            <button v-else type="button" :class="{ active: page === number }" @click="selectPage(number)">{{ number }}</button>
          </template>
        </div>
        <button type="button" class="page-arrow" :disabled="page === result?.totalPages" aria-label="下一页" @click="selectPage(page + 1)"><svg viewBox="0 0 20 20"><path d="m7.5 5 5 5-5 5"/></svg></button>
        <span class="page-meta">第 {{ page }} / {{ result?.totalPages }} 页 · 每页 {{ result?.pageSize }} 天</span>
      </nav>
    </template>
  </section>
</template>

<style scoped>
.news-search { --blue:#0071e3; --ink:#1d1d1f; --muted:#86868b; --surface:#fff; --field:#e3e3e8; --divider:#e5e5ea; width:100%; max-width:760px; margin:0 auto; color:var(--ink); }
.component-header { padding:0 8px; margin-bottom:24px; }.component-header h1{font-size:28px;line-height:1.15;letter-spacing:-.03em;margin:0 0 4px}.component-header p{font-size:15px;font-weight:500;color:var(--muted);margin:0}
.toolbar{display:flex;flex-direction:column;gap:12px;padding:0 8px;margin-bottom:22px}.primary-controls{display:flex;align-items:center;gap:12px}.search-box{position:relative;flex:1;min-width:220px}.search-box svg{position:absolute;left:12px;top:10px;width:16px;height:16px;fill:none;stroke:var(--muted);stroke-width:2;stroke-linecap:round}.search-box input{width:100%;height:36px;border:0;border-radius:10px;background:var(--field);padding:0 12px 0 36px;font-size:15px;font-weight:500;color:var(--ink);outline:none;box-shadow:0 1px 2px #0000000a}.search-box input:focus{background:#fff;box-shadow:0 0 0 2px var(--blue)}.date-range{display:flex;align-items:center;gap:8px;flex-shrink:0}.date-range label{display:block}.date-range input{width:135px;height:36px;border:0;border-radius:10px;background:var(--field);padding:0 10px;color:var(--ink);font-size:13px;font-weight:600;outline:none;box-shadow:0 1px 2px #0000000a}.date-range input:focus{background:#fff;box-shadow:0 0 0 2px var(--blue)}.date-range>span{color:var(--muted);font-size:13px}.secondary-controls{display:flex;align-items:center;justify-content:space-between;gap:12px}.segments{display:flex;padding:2px;border-radius:9px;background:var(--field);box-shadow:0 1px 2px #0000000a}.segments button{border:0;background:transparent;border-radius:7px;padding:6px 16px;color:#6e6e73;font-size:13px;font-weight:600;cursor:pointer}.segments button.active{background:#fff;color:var(--ink);box-shadow:0 1px 3px #0000001a}.ai-button{display:flex;gap:6px;align-items:center;border:1px solid #d2d2d7;background:#fff;border-radius:9px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer}.ai-button svg{width:14px;height:14px;fill:none;stroke:currentColor;stroke-width:2}.ai-button.active{color:#fff;border-color:transparent;background:linear-gradient(90deg,#0071e3,#8b5cf6)}.ai-panel{width:100%;height:600px;margin-top:16px;overflow:hidden;border:1px solid var(--divider);border-radius:14px;background:#fff;box-shadow:0 1px 4px #0000000d}.ai-panel iframe{width:100%;height:100%;border:0}.ai-placeholder{position:relative;display:grid;width:100%;height:100%;place-items:center;background:#fafafa}.placeholder-border{position:absolute;inset:16px;border:2px dashed #d2d2d7;border-radius:14px}.placeholder-content{position:relative;z-index:1;max-width:360px;padding:16px;text-align:center}.placeholder-icon{display:grid;width:56px;height:56px;margin:0 auto 16px;place-items:center;border-radius:14px;color:#fff;background:linear-gradient(135deg,#0071e3,#8b5cf6);box-shadow:0 8px 24px #8b5cf633}.placeholder-icon svg{width:28px;height:28px;fill:none;stroke:currentColor;stroke-width:2}.placeholder-content h2{margin:0 0 8px;font-size:18px;letter-spacing:-.02em}.placeholder-content p{margin:0;color:var(--muted);font-size:14px;line-height:1.65}.placeholder-content code{color:#6e6e73}
.summary{min-height:32px;padding:0 8px;margin-bottom:12px;display:flex;justify-content:space-between;align-items:center;font-size:13px;font-weight:500;color:var(--muted)}.summary strong{color:var(--ink)}.degraded{color:#9a6700}.day-list{transition:opacity .2s}.day-list.refreshing{opacity:.55;pointer-events:none}.day-section{margin-bottom:30px}.day-header{display:flex;align-items:flex-end;gap:8px;padding:0 16px;margin-bottom:8px}.day-header h2{font-size:20px;letter-spacing:-.02em;margin:0}.day-header span{font-size:13px;font-weight:500;color:var(--muted);margin-bottom:2px}.day-card{overflow:hidden;background:var(--surface);border-radius:14px;box-shadow:0 1px 4px #0000000d,0 0 0 1px #0000000d}.result-row{display:flex;gap:14px;padding:16px}.result-row+.result-row{border-top:1px solid var(--divider)}.item-meta{flex:0 0 52px;display:flex;align-items:flex-end;flex-direction:column;gap:6px;padding-top:2px}.item-meta time{font-size:12px;font-weight:600;color:var(--muted);font-variant-numeric:tabular-nums}.type-badge{display:inline-flex;align-items:center;justify-content:center;width:36px;height:18px;border-radius:4px;font-size:10px;font-weight:700;letter-spacing:.08em}.type-badge.news{background:#e8f0fb;color:var(--blue)}.type-badge.announcement{background:#fffbeb;color:#a16207;border:1px solid #fde68a}.item-body{flex:1;min-width:0}.item-title{display:block;width:100%;border:0;background:transparent;padding:0;text-align:left;color:var(--ink);font-size:16px;line-height:1.3;font-weight:650;letter-spacing:-.02em;cursor:pointer}.item-title:hover{color:var(--blue)}mark{background:#fef08a;color:inherit;border-radius:2px;padding:0 1px}.byline{display:flex;align-items:center;gap:6px;margin:6px 0 8px;font-size:12px;font-weight:500;color:var(--muted)}.byline strong{color:var(--ink)}.byline i{font-style:normal;color:#d2d2d7}.snippet{font-size:14px;line-height:1.55;letter-spacing:-.01em;color:#6e6e73;margin:0;white-space:pre-wrap}.snippet.collapsed{display:-webkit-box;overflow:hidden;-webkit-line-clamp:2;-webkit-box-orient:vertical}.announcement-html{position:relative;overflow:hidden;color:#6e6e73;font-size:14px;line-height:1.55;letter-spacing:-.01em;transition:max-height .3s ease}.announcement-html.collapsed{max-height:62px;mask-image:linear-gradient(to bottom,#000 40%,transparent 100%);-webkit-mask-image:linear-gradient(to bottom,#000 40%,transparent 100%)}.announcement-html:not(.collapsed){max-height:2000px}:deep(.announcement-html p){margin:0 0 8px}:deep(.announcement-html p:last-child){margin-bottom:0}:deep(.announcement-html ul),:deep(.announcement-html ol){margin:4px 0;padding-left:22px}:deep(.announcement-html img){max-width:100%;height:auto}:deep(.announcement-html mark.search-hit){background:#fef08a;color:inherit;border-radius:2px;padding:0 1px}.expand-button{display:flex;align-items:center;gap:2px;border:0;background:transparent;padding:6px 0 0;color:var(--blue);font-size:13px;font-weight:600;cursor:pointer}.expand-button svg{width:14px;height:14px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round;transition:transform .2s}.expand-button svg.rotated{transform:rotate(180deg)}.cover-image{flex:0 0 100px;width:100px;height:72px;overflow:hidden;border:1px solid var(--divider);border-radius:8px;background:#f5f5f7}.cover-image img{display:block;width:100%;height:100%;object-fit:cover;transition:transform .5s ease}.result-row:hover .cover-image img{transform:scale(1.05)}
.state-card{display:flex;min-height:220px;align-items:center;justify-content:center;flex-direction:column;gap:10px;color:var(--muted);text-align:center}.state-card p{margin:0}.state-card button{border:0;border-radius:999px;padding:8px 16px;cursor:pointer}.empty-state svg{width:58px;height:58px;padding:14px;border-radius:50%;background:var(--field);fill:none;stroke:var(--muted);stroke-width:2}.empty-state strong{font-size:17px;color:var(--ink)}.empty-state span{font-size:14px}.pagination{display:flex;align-items:center;justify-content:center;flex-wrap:wrap;gap:8px;margin:38px 0 20px}.pagination button{cursor:pointer}.page-arrow,.page-numbers{background:#fff;box-shadow:0 1px 4px #0000000d,0 0 0 1px #0000000d}.page-arrow{display:grid;place-items:center;width:32px;height:32px;border:0;border-radius:50%}.page-arrow:disabled{opacity:.3;cursor:not-allowed}.page-arrow svg{width:16px;height:16px;fill:none;stroke:currentColor;stroke-width:2;stroke-linecap:round;stroke-linejoin:round}.page-numbers{display:flex;gap:6px;align-items:center;border-radius:999px;padding:4px 8px}.page-numbers button{width:32px;height:32px;border:0;border-radius:50%;background:transparent;font-size:14px;font-weight:600}.page-numbers button.active{background:var(--ink);color:#fff}.page-numbers span{width:24px;text-align:center;color:var(--muted)}.page-meta{flex-basis:100%;color:var(--muted);font-size:12px;font-weight:600;text-align:center}.sr-only{position:absolute;width:1px;height:1px;padding:0;margin:-1px;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0}
@media(max-width:700px){.primary-controls{align-items:stretch;flex-direction:column}.search-box{min-width:0}.date-range{width:100%}.date-range label{flex:1}.date-range input{width:100%}.secondary-controls{justify-content:space-between}.segments{flex:1}.segments button{flex:1;padding-inline:10px}.component-header h1{font-size:25px}.result-row{gap:10px;padding:14px 12px}.item-meta{flex-basis:44px}.day-header{padding-inline:8px}.day-header span{font-size:12px}.summary{align-items:flex-start;gap:6px;flex-direction:column}.ai-panel{height:520px}}
@media(max-width:430px){.ai-button{padding-inline:10px}.cover-image{flex-basis:80px;width:80px;height:60px}.page-numbers{gap:2px;padding-inline:4px}.page-numbers button{width:30px;height:30px}}
</style>
