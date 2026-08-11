# 新闻公告检索组件

该目录是可独立嵌入现有门户的 Vue 3 组件实现，构建工具固定为 Vite 4.4.9。入口组件为 `src/components/NewsAnnouncementSearch.vue`，演示页 `App.vue` 只用于本地预览，不属于门户壳。

组件调用 `POST /api/v1/search/day-groups`，按后端返回的 `days` 直接渲染。前端不会下载全量结果、按日重组或再次切片；切换页码时使用后端返回的 `totalPages` 重新请求。

## 本地运行

```sh
npm install
npm run dev
```

Vite 的开发服务器和 `vite preview` 都会将 `/api` 代理到 `http://localhost:5000`。可在启动前通过 `AURASEARCH_API_URL` 修改目标地址。启动前请确认 `http://localhost:5000/health/live` 可以访问。

## 门户接入

```vue
<script setup lang="ts">
import NewsAnnouncementSearch from './components/NewsAnnouncementSearch.vue'
</script>

<template>
  <NewsAnnouncementSearch
    endpoint="/api/v1/search/day-groups"
    :days-per-page="5"
  />
</template>
```

AI 检索按钮始终按 Figma Make 设计显示。可选 `ai-url` 传入真实 AI 检索页面地址；未提供时点击按钮显示设计中的 iframe 占位面板。
