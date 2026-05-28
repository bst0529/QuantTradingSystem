<template>
  <div class="dashboard-container">
    <h2>📈 0050 量化交易與 AI 情緒回測系統</h2>
    
    <div v-if="loading" class="loading-container">
      <div class="spinner"></div>
      <p>正在從後端載入量化數據與圖表...</p>
    </div>

    <div v-if="haveErrorMsg" class="error-container">
      <p>⚠️ {{ errorMsg }}</p>
    </div>

    <div v-if="!loading && !haveErrorMsg" class="ai-card">
      <div class="ai-header">
        <span class="ai-icon">⚡</span> 
        <h3>Groq AI 盤後速報</h3>
        <span v-if="isAiLoading" class="ai-loading-text"> (正在生成今日講評...)</span>
      </div>
      <p class="ai-content">{{ latestAiAdvice }}</p>
    </div>

    <div ref="chartRef" class="chart-box" v-show="!loading && !haveErrorMsg"></div>
  </div>
</template>

<script setup>
// 💡 關鍵新增：引入 nextTick
import { ref, onMounted, nextTick } from 'vue'
import * as echarts from 'echarts'

const chartRef = ref(null)
const loading = ref(true)
const haveErrorMsg = ref(false)
const errorMsg = ref('')

const latestAiAdvice = ref('AI 正在分析今日盤勢...')
const isAiLoading = ref(true)

onMounted(async () => {
  try {
    // 動態取得今天日期
    const today = new Date();
    const year = today.getFullYear();
    const month = String(today.getMonth() + 1).padStart(2, '0');
    const day = String(today.getDate()).padStart(2, '0');
    const endDate = `${year}-${month}-${day}`;
    const startDate = '2023-01-01';

    const apiUrl = `https://quant-api.politehill-187dbd7a.koreacentral.azurecontainerapps.io/api/strategy/0050?startDate=${startDate}&endDate=${endDate}`;
    const response = await fetch(apiUrl);

    if (!response.ok) {
      const errorData = await response.json()
      errorMsg.value = errorData.message || "取得資料失敗"
      haveErrorMsg.value = true
      loading.value = false
      return
    }
    
    const apiData = await response.json()
    const dataList = apiData.results || []

    // 如果資料庫是空的 (Worker 還沒跑)
    if (dataList.length === 0) {
      errorMsg.value = "資料庫目前沒有數據，請確認後端 Worker 已經順利執行抓取。"
      haveErrorMsg.value = true
      loading.value = false
      return
    }

    // 呼叫 AI
    const lastDay = dataList[dataList.length - 1]
    const lastDate = String(lastDay.date || lastDay.Date).split('T')[0]
    const closePrice = Number(lastDay.close || lastDay.Close).toFixed(2)
    const ma60 = Number(lastDay.mA60 || lastDay.MA60).toFixed(2)
    const kValue = Number(lastDay.k || lastDay.K).toFixed(2)
    const dValue = Number(lastDay.d || lastDay.D).toFixed(2)
    
    const marketDataStr = `日期:${lastDate}, 收盤價:${closePrice}, 季線(MA60):${ma60}, K值:${kValue}, D值:${dValue}`

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

    // 整理圖表資料
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

    // 🌟 核心修復區：先解除 loading，再等畫面更新，最後才畫圖！
    loading.value = false;
    await nextTick(); // 等待 Vue 把 v-show="true" 渲染到 DOM 上

    const myChart = echarts.init(chartRef.value)
    
    const option = {
      tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
      axisPointer: { link: [{ xAxisIndex: 'all' }] }, 
      grid: [
        { left: '10%', right: '8%', top: '5%', height: '40%' }, 
        { left: '10%', right: '8%', top: '50%', height: '12%' }, 
        { left: '10%', right: '8%', top: '67%', height: '12%' }, 
        { left: '10%', right: '8%', top: '84%', height: '12%' }  
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
        { name: '0050 K線', type: 'candlestick', data: kLineData, itemStyle: { color: '#ef232a', color0: '#14b143', borderColor: '#ef232a', borderColor0: '#14b143' } },
        { name: 'MA60 季線', type: 'line', data: ma60Data, smooth: true, symbol: 'none', lineStyle: { color: '#e056fd', width: 2 }, itemStyle: { color: '#e056fd' } },
        { name: '成交量', type: 'bar', xAxisIndex: 1, yAxisIndex: 1, data: volumes },
        { name: 'K 值', type: 'line', xAxisIndex: 2, yAxisIndex: 2, data: kData, symbol: 'none', lineStyle: { color: '#f1c40f', width: 1.5 }, itemStyle: { color: '#f1c40f' } },
        { name: 'D 值', type: 'line', xAxisIndex: 2, yAxisIndex: 2, data: dData, symbol: 'none', lineStyle: { color: '#3498db', width: 1.5 }, itemStyle: { color: '#3498db' } },
        { name: 'AI情緒', type: 'line', xAxisIndex: 3, yAxisIndex: 3, data: sentiments, smooth: true, symbol: 'none', lineStyle: { color: '#e67e22', width: 1.5 }, itemStyle: { color: '#e67e22' } }
      ]
    }

    myChart.setOption(option)
    window.addEventListener('resize', () => myChart.resize())

  } catch (error) {
    console.error(error)
    errorMsg.value = "無法連線至伺服器或發生未知的錯誤！"
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

h2 { margin-top: 0; margin-bottom: 16px; }

/* ⏳ Loading 區塊樣式 */
.loading-container {
  flex-grow: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  color: #00ffcc;
}

.spinner {
  border: 4px solid rgba(255, 255, 255, 0.1);
  border-top: 4px solid #00ffcc;
  border-radius: 50%;
  width: 50px;
  height: 50px;
  animation: spin 1s linear infinite;
  margin-bottom: 16px;
}

@keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }

/* ❌ 錯誤區塊樣式 */
.error-container {
  flex-grow: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  color: #ef232a;
  font-size: 1.2rem;
  font-weight: bold;
}

.ai-card {
  background: linear-gradient(135deg, #232336 0%, #2a2a40 100%);
  border-left: 4px solid #00ffcc;
  border-radius: 8px;
  padding: 16px 20px;
  margin-bottom: 16px;
  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.2);
  flex-shrink: 0;
}

.ai-header { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
.ai-header h3 { margin: 0; font-size: 1.1rem; color: #00ffcc; letter-spacing: 1px; }
.ai-loading-text { font-size: 0.9rem; color: #aaaaaa; animation: pulse 1.5s infinite; }
.ai-content { margin: 0; line-height: 1.5; font-size: 1rem; color: #e0e0e0; }

.chart-box {
  width: 100%;
  flex-grow: 1;
}

@keyframes pulse { 0% { opacity: 0.5; } 50% { opacity: 1; } 100% { opacity: 0.5; } }
</style>