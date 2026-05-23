<template>
  <div class="dashboard-container">
    <h2>📈 0050 量化交易與 AI 情緒回測系統</h2>
    <div v-if="loading" class="loading">資料載入中...請確認後端 API 已啟動</div>
    <div v-if="haveErrorMsg" class="loading">{{ errorMsg }}</div>
    <div ref="chartRef" class="chart-box"></div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'
import * as echarts from 'echarts'

const chartRef = ref(null)
const loading = ref(true)
const haveErrorMsg = ref(true)
const errorMsg = ref('')

onMounted(async () => {
  try {
    const response = await fetch('http://localhost:5050/api/strategy/0050?startDate=2023-01-01&endDate=2026-05-07')

    if (!response.ok) {
      const errorData = await response.json()
      errorMsg.value = errorData.message;
      haveErrorMsg.value = true;
      loading.value = false
      return
    } else {
      haveErrorMsg.value = false;
    }
    
    const apiData = await response.json()
    const dataList = apiData.results || []

    // 1. 整理基礎資料
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

    // 🌟 2. 提取新武器：MA60 與 KD 指標
    const ma60Data = dataList.map(item => Number(item.mA60 ?? item.MA60 ?? 0))
    const kData = dataList.map(item => Number(item.k ?? item.K ?? 50))
    const dData = dataList.map(item => Number(item.d ?? item.D ?? 50))

    // 🌟 3. 提取買賣訊號 (MarkPoint 標記)
    const markPointData = []
    dataList.forEach((item, index) => {
      const sig = item.signal ?? item.Signal ?? 0
      // 假設 C# Enum: 1 = Buy, 2 = Sell (如果回傳是字串則比對 'Buy' / 'Sell')
      if (sig === 1 || sig === 'Buy' || sig === 'buy') {
        markPointData.push({
          name: '買進',
          coord: [index, Number(item.low ?? item.Low)], // 標記在 K 棒下方
          value: '買',
          itemStyle: { color: '#ef232a' },
          symbolOffset: [0, 20] // 向下偏移，避免擋住 K 棒
        })
      } else if (sig === 2 || sig === 'Sell' || sig === 'sell') {
        markPointData.push({
          name: '賣出',
          coord: [index, Number(item.high ?? item.High)], // 標記在 K 棒上方
          value: '賣',
          itemStyle: { color: '#14b143' },
          symbolOffset: [0, -20] // 向上偏移
        })
      }
    })

    const myChart = echarts.init(chartRef.value)
    loading.value = false

    // 4. 設定四圖連動佈局
    const option = {
      tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
      axisPointer: { link: [{ xAxisIndex: 'all' }] }, // 十字線全域連動
      grid: [
        { left: '10%', right: '8%', top: '5%', height: '40%' },   // Grid 0: K線 + MA60
        { left: '10%', right: '8%', top: '50%', height: '12%' },  // Grid 1: 成交量
        { left: '10%', right: '8%', top: '67%', height: '12%' },  // Grid 2: KD 指標
        { left: '10%', right: '8%', top: '84%', height: '12%' }   // Grid 3: AI 情緒
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
        // 第一層：K 線與買賣標籤
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
        // 第一層疊加：MA60 季線
        {
          name: 'MA60 季線',
          type: 'line',
          data: ma60Data,
          smooth: true,
          symbol: 'none', // 隱藏小圓點，讓線條更乾淨
          lineStyle: { color: '#e056fd', width: 2 }, // 亮紫色
          itemStyle: { color: '#e056fd' }
        },
        // 第二層：成交量
        {
          name: '成交量',
          type: 'bar',
          xAxisIndex: 1,
          yAxisIndex: 1,
          data: volumes
        },
        // 第三層：KD 指標 (K線黃、D線藍)
        {
          name: 'K 值',
          type: 'line',
          xAxisIndex: 2,
          yAxisIndex: 2,
          data: kData,
          symbol: 'none',
          lineStyle: { color: '#f1c40f', width: 1.5 }, // 黃色
          itemStyle: { color: '#f1c40f' }
        },
        {
          name: 'D 值',
          type: 'line',
          xAxisIndex: 2,
          yAxisIndex: 2,
          data: dData,
          symbol: 'none',
          lineStyle: { color: '#3498db', width: 1.5 }, // 藍色
          itemStyle: { color: '#3498db' }
        },
        // 第四層：AI 情緒
        {
          name: 'AI情緒',
          type: 'line',
          xAxisIndex: 3,
          yAxisIndex: 3,
          data: sentiments,
          smooth: true,
          symbol: 'none',
          lineStyle: { color: '#e67e22', width: 1.5 }, // 橘色
          itemStyle: { color: '#e67e22' }
        }
      ]
    }

    myChart.setOption(option)
    window.addEventListener('resize', () => myChart.resize())

  } catch (error) {
    console.error(error)
    alert("無法載入資料，請確認後端 API 已啟動！")
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
}
.loading {
  text-align: center;
  margin-top: 50px;
  font-size: 1.2rem;
  color: #ff9800;
}
.chart-box {
  width: 100%;
  height: calc(100% - 60px);
}
</style>