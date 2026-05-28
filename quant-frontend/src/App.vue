<template>
  <div class="dashboard-container">
    <h2>📈 0050 量化交易與 AI 情緒回測系統</h2>
    
    <div v-if="loading" class="loading">資料載入中...請確認後端 API 已啟動</div>
    <div v-if="haveErrorMsg" class="loading error">{{ errorMsg }}</div>

    <div v-if="!loading && !haveErrorMsg" class="ai-card">
      <div class="ai-header">
        <span class="ai-icon">⚡</span> 
        <h3>Groq AI 盤後速報</h3>
        <span v-if="isAiLoading" class="ai-loading-text"> (正在生成今日講評...)</span>
      </div>
      <p class="ai-content">{{ latestAiAdvice }}</p>
    </div>

    <div ref="chartRef" class="chart-box" :class="{ 'show-chart': !loading && !haveErrorMsg }"></div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import * as echarts from 'echarts'

const chartRef = ref(null)
const loading = ref(true)
const haveErrorMsg = ref(false)
const errorMsg = ref('')

// AI 講評相關變數
const latestAiAdvice = ref('AI 正在分析今日盤勢...')
const isAiLoading = ref(true)

onMounted(async () => {
  try {
    // 1. 取得量化交易歷史數據
    const response = await fetch('https://quant-api.purplesmoke-0c574d76.koreacentral.azurecontainerapps.io/api/strategy/0050?startDate=2023-01-01&endDate=2026-05-07')

    if (!response.ok) {
      const errorData = await response.json()
      errorMsg.value = errorData.message || "取得資料失敗"
      haveErrorMsg.value = true
      loading.value = false
      return
    }
    
    const apiData = await response.json()
    const dataList = apiData.results || []

    if (dataList.length > 0) {
      // 💡 抓取最後一天的資料，準備餵給 AI
      const lastDay = dataList[dataList.length - 1]
      const lastDate = String(lastDay.date || lastDay.Date).split('T')[0]
      const closePrice = Number(lastDay.close || lastDay.Close).toFixed(2)
      const ma60 = Number(lastDay.mA60 || lastDay.MA60).toFixed(2)
      const kValue = Number(lastDay.k || lastDay.K).toFixed(2)
      const dValue = Number(lastDay.d || lastDay.D).toFixed(2)
      
      // 組合給 AI 的 Prompt 字串
      const marketDataStr = `日期:${lastDate}, 收盤價:${closePrice}, 季線(MA60):${ma60}, K值:${kValue}, D值:${dValue}`

      // 💡 呼叫你的 Azure Function (Groq AI)
      fetch('https://fn-quant-ai-2026.azurewebsites.net/api/getaiadvice', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ marketData: marketDataStr })
      })
      .then(res => res.json())
      .then(data => {
        latestAiAdvice.value = data.advice
        isAiLoading.value = false
      })
      .catch(err => {
        console.error("AI 呼叫失敗:", err)
        latestAiAdvice.value = "目前無法連線至 AI 伺服器，建議維持既有防禦性策略。"
        isAiLoading.value = false
      })
    }

    // 2. 整理 ECharts 基礎資料
    const dates = dataList.map(item => String(item.date || item.Date).split('T')[0])
    
    const kLineData = dataList.map(item => [
      Number(item.open ?? item.Open ?? 0),
      Number(item.close ?? item.Close ?? 0),
      Number(item.low ?? item.Low ?? 0),
      Number(item.high ?? item.High ?? 0)
    ])

    const volumes = dataList.map((item) => {
      const c = Number(item.close ?? item.Close ?? 0)
      const o = Number(item.open ?? item.Open ?? 0)
      return { value: Number(item.volume ?? item.Volume ?? 0), itemStyle: { color: c >= o ? '#ef232a' : '#14b143' } }
    })

    const sentiments = dataList.map(item => Number(item.sentimentScore ?? item.SentimentScore ?? 0.5))
    const ma60Data = dataList.map(item => Number(item.mA60 ?? item.MA60 ?? 0))
    const kData = dataList.map(item => Number(item.k ?? item.K ?? 50))
    const dData = dataList.map(item => Number(item.d ?? item.D ?? 50))

    // 3. 提取買賣訊號 (MarkPoint 標記)
    const markPointData = []
    dataList.forEach((item, index) => {
      const sig = item.signal ?? item.Signal ?? 0
      if (sig === 1 || String(sig).toLowerCase() === 'buy') {
        markPointData.push({
          name: '買進',
          coord: [index, Number(item.low ?? item.Low)], 
          value: '買',
          itemStyle: { color: '#ef232a' },
          symbolOffset: [0, 20] 
        })
      } else if (sig === 2 || String(sig).toLowerCase() === 'sell') {
        markPointData.push({
          name: '賣出',
          coord: [index, Number(item.high ?? item.High)], 
          value: '賣',
          itemStyle: { color: '#14b143' },
          symbolOffset: [0, -20] 
        })
      }
    })

    const myChart = echarts.init(chartRef.value)
    loading.value = false

    // 4. 設定四圖連動佈局
    const option = {
      tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
      axisPointer: { link: [{ xAxisIndex: 'all' }] }, 
      grid: [
        { left: '10%', right: '8%', top: '5%', height: '40%' },   // K線 + MA60
        { left: '10%', right: '8%', top: '50%', height: '12%' },  // 成交量
        { left: '10%', right: '8%', top: '67%', height: '12%' },  // KD 指標
        { left: '10%', right: '8%', top: '84%', height: '12%' }   // AI 情緒
      ],
      xAxis: [
        { type: 'category', data: dates, gridIndex: 0, axisLabel: { show: false } }, 
        { type: 'category', data: dates, gridIndex: 1, axisLabel: { show: false } },
        { type: 'category', data: dates, gridIndex: 2, axisLabel: { show: false } },
        { type: 'category', data: dates, gridIndex: 3, axisLabel: { color: '#cccccc' } }
      ],
      yAxis: [
        { scale: true, gridIndex: 0, splitArea: { show: true }, axisLabel: { color: '#cccccc' }, splitLine: { lineStyle: { color: '#333' } } },
        { scale: true, gridIndex: 1, splitNumber: 2, axisLabel: { show: false }, splitLine: { show: false } },
        { scale: true, gridIndex: 2, splitNumber: 2, axisLabel: { color: '#cccccc' }, splitLine: { lineStyle: { color: '#333' } } },
        { scale: true, gridIndex: 3, splitNumber: 2, axisLabel: { color: '#cccccc' }, splitLine: { lineStyle: { color: '#333' } } }
      ],
      dataZoom: [{ type: 'inside', xAxisIndex: [0, 1, 2, 3], start: 50, end: 100 }],
      series: [
        {
          name: '0050 K線',
          type: 'candlestick',
          data: kLineData,
          itemStyle: { color: '#ef232a', color0: '#14b143', borderColor: '#ef232a', borderColor0: '#14b143' },
          markPoint: {
            data: markPointData,
            label: { color: '#fff', fontSize: 12, fontWeight: 'bold' }
          }
        },
        {
          name: 'MA60 季線',
          type: 'line',
          data: ma60Data,
          smooth: true,
          symbol: 'none', 
          lineStyle: { color: '#e056fd', width: 2 }, 
          itemStyle: { color: '#e056fd' }
        },
        {
          name: '成交量',
          type: 'bar',
          xAxisIndex: 1,
          yAxisIndex: 1,
          data: volumes
        },
        {
          name: 'K 值',
          type: 'line',
          xAxisIndex: 2,
          yAxisIndex: 2,
          data: kData,
          symbol: 'none',
          lineStyle: { color: '#f1c40f', width: 1.5 }, 
          itemStyle: { color: '#f1c40f' }
        },
        {
          name: 'D 值',
          type: 'line',
          xAxisIndex: 2,
          yAxisIndex: 2,
          data: dData,
          symbol: 'none',
          lineStyle: { color: '#3498db', width: 1.5 }, 
          itemStyle: { color: '#3498db' }
        },
        {
          name: 'AI情緒',
          type: 'line',
          xAxisIndex: 3,
          yAxisIndex: 3,
          data: sentiments,
          smooth: true,
          symbol: 'none',
          lineStyle: { color: '#e67e22', width: 1.5 }, 
          itemStyle: { color: '#e67e22' }
        }
      ]
    }

    myChart.setOption(option)
    window.addEventListener('resize', () => myChart.resize())

  } catch (error) {
    console.error(error)
    errorMsg.value = "無法載入資料，請確認後端 API 已啟動！"
    haveErrorMsg.value = true
    loading.value = false
  }
})
</script>

<style scoped>
.dashboard-container {
  width: 100vw;
  height: 100vh;
  padding: 20px;
  background-color: #1e1e1e;
  color: white;
  font-family: sans-serif;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
}

h2 {
  margin-top: 0;
  margin-bottom: 16px;
}

.loading {
  text-align: center;
  margin-top: 50px;
  font-size: 1.2rem;
  color: #ff9800;
}

.error {
  color: #ef232a;
}

/* 🤖 AI 卡片專屬樣式 */
.ai-card {
  background: linear-gradient(135deg, #232336 0%, #2a2a40 100%);
  border-left: 4px solid #00ffcc;
  border-radius: 8px;
  padding: 16px 20px;
  margin-bottom: 16px;
  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.2);
  flex-shrink: 0; /* 防止卡片被壓縮 */
}

.ai-header {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.ai-header h3 {
  margin: 0;
  font-size: 1.1rem;
  color: #00ffcc;
  letter-spacing: 1px;
}

.ai-loading-text {
  font-size: 0.9rem;
  color: #aaaaaa;
  animation: pulse 1.5s infinite;
}

.ai-content {
  margin: 0;
  line-height: 1.5;
  font-size: 1rem;
  color: #e0e0e0;
}

/* 讓圖表自動填滿剩餘空間 */
.chart-box {
  width: 100%;
  flex-grow: 1; /* 佔滿剩餘高度 */
  display: none;
}

.show-chart {
  display: block;
}

@keyframes pulse {
  0% { opacity: 0.5; }
  50% { opacity: 1; }
  100% { opacity: 0.5; }
}
</style>