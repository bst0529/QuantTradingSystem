<template>
  <div class="dashboard">
    <header>
      <h1>📈 0050 量化交易與 AI 情緒回測系統</h1>
    </header>

    <div class="ai-card" v-if="aiAdvice || aiLoading">
      <div class="ai-title">⚡ Groq AI 盤後速報</div>
      <div class="ai-content">
        <span v-if="aiLoading" class="typing-effect">正在連線 Groq AI 運算大腦生成決策中...</span>
        <span v-else>{{ aiAdvice }}</span>
      </div>
    </div>

    <div v-if="loading" class="loading-container">
      <div class="spinner"></div>
      <p>資料載入與圖表渲染中...請稍候</p>
    </div>

    <div v-else-if="errorMsg" class="error-msg">
      ⚠️ {{ errorMsg }}
    </div>

    <div v-show="!loading && !errorMsg" ref="chartRef" class="chart-box"></div>
  </div>
</template>

<script setup>
import { ref, onMounted, nextTick } from 'vue';
import * as echarts from 'echarts';

// 狀態變數
const chartRef = ref(null);
const loading = ref(true);
const aiLoading = ref(false);
const errorMsg = ref('');
const aiAdvice = ref('');

const getTodayString = () => {
  const today = new Date();
  const year = today.getFullYear();
  const month = String(today.getMonth() + 1).padStart(2, '0');
  const day = String(today.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

// 💡 1. 輔助函數：計算 60MA
const calculateMA = (dayCount, data) => {
  const result = [];
  for (let i = 0; i < data.length; i++) {
    if (i < dayCount - 1) {
      result.push('-');
      continue;
    }
    let sum = 0;
    for (let j = 0; j < dayCount; j++) sum += data[i - j][1]; // 索引 1 為收盤價
    result.push((sum / dayCount).toFixed(2));
  }
  return result;
};

// 💡 2. 輔助函數：前端自行計算 9 日 KD (終極容錯機制)
const calculateKD = (data) => {
  const kValues = [];
  const dValues = [];
  let prevK = 50;
  let prevD = 50;

  for (let i = 0; i < data.length; i++) {
    if (i < 8) { // 前 8 天沒有足夠區間，預設 50
      kValues.push(50);
      dValues.push(50);
      continue;
    }
    let highestHigh = -Infinity;
    let lowestLow = Infinity;
    for (let j = 0; j < 9; j++) {
      const high = data[i - j][3]; // 索引 3 為最高價
      const low = data[i - j][2];  // 索引 2 為最低價
      if (high > highestHigh) highestHigh = high;
      if (low < lowestLow) lowestLow = low;
    }
    
    const close = data[i][1];
    let rsv = 50;
    if (highestHigh !== lowestLow) {
      rsv = ((close - lowestLow) / (highestHigh - lowestLow)) * 100;
    }

    const k = (2 / 3) * prevK + (1 / 3) * rsv;
    const d = (2 / 3) * prevD + (1 / 3) * k;

    kValues.push(parseFloat(k.toFixed(2)));
    dValues.push(parseFloat(d.toFixed(2)));
    prevK = k;
    prevD = d;
  }
  return { kValues, dValues };
};

onMounted(async () => {
  try {
    const startDate = '2023-01-01';
    const endDate = getTodayString();
    
    const response = await fetch(`https://quant-api.delightfulforest-8bb7d871.koreacentral.azurecontainerapps.io/api/strategy/0050?startDate=${startDate}&endDate=${endDate}`);
    if (!response.ok) throw new Error(`API 請求失敗: ${response.status}`);
    
    const jsonResponse = await response.json();
    const data = Array.isArray(jsonResponse) 
      ? jsonResponse 
      : (jsonResponse.results || jsonResponse.data || jsonResponse.items || jsonResponse.result);
    
    if (!data || !Array.isArray(data) || data.length === 0) {
      throw new Error("查無資料，請確認資料庫是否已寫入數據。");
    }

    const dates = [];
    const values = []; 
    const volumes = [];
    const aiScores = [];
    
    // 第一階段：萃取必定存在的基礎 K 線數據
    data.forEach(item => {
      dates.push(item.date.split('T')[0]);
      values.push([item.open, item.close, item.low, item.high]);
      volumes.push(item.volume);
      aiScores.push(item.sentimentScore || 0.5);
    });

    const ma60Data = calculateMA(60, values);
    
    // 🛡️ 雙重保險：呼叫前端 KD 引擎
    const { kValues: fallbackK, dValues: fallbackD } = calculateKD(values);

    const kValues = [];
    const dValues = [];
    
    // 第二階段：萃取 KD (支援各種 C# 命名格式，若都沒有則無縫切換到前端計算的數值)
    data.forEach((item, index) => {
      const k = item.kValue ?? item.KValue ?? item.k ?? item.K ?? fallbackK[index];
      const d = item.dValue ?? item.DValue ?? item.d ?? item.D ?? fallbackD[index];
      kValues.push(k);
      dValues.push(d);
    });

    // 3. 計算 KD 黃金/死亡交叉買賣點
    const markPointData = [];
    for (let i = 1; i < dates.length; i++) {
      const prevK = kValues[i - 1];
      const prevD = dValues[i - 1];
      const currK = kValues[i];
      const currD = dValues[i];

      if (prevK <= prevD && currK > currD) {
        markPointData.push({
          name: '買進',
          coord: [dates[i], values[i][2]], // 標在當天最低價 (Low)
          value: '買',
          itemStyle: { color: '#ff4757' },
          symbolOffset: [0, 20] 
        });
      }
      else if (prevK >= prevD && currK < currD) {
        markPointData.push({
          name: '賣出',
          coord: [dates[i], values[i][3]], // 標在當天最高價 (High)
          value: '賣',
          itemStyle: { color: '#2ed573' },
          symbolRotate: 180,
          symbolOffset: [0, -20]
        });
      }
    }

    loading.value = false;
    await nextTick();
    
    if (chartRef.value) {
      const myChart = echarts.init(chartRef.value, 'dark');
      
      const option = {
        backgroundColor: '#1e1e1e',
        tooltip: { trigger: 'axis', axisPointer: { type: 'cross' } },
        legend: { data: ['0050 K線', 'MA60', 'K', 'D', 'AI Score'], top: 0 },
        axisPointer: { link: [{ xAxisIndex: 'all' }] },
        // 💡 稍微微調了比例，讓 4 個圖表都有呼吸空間
        grid: [
          { left: '10%', right: '8%', top: '5%', height: '40%' },   // K 線與 MA60
          { left: '10%', right: '8%', top: '50%', height: '12%' },  // 成交量
          { left: '10%', right: '8%', top: '67%', height: '12%' },  // KD
          { left: '10%', right: '8%', top: '84%', height: '10%' }   // AI
        ],
        xAxis: [
          { type: 'category', data: dates, gridIndex: 0, boundaryGap: false },
          { type: 'category', data: dates, gridIndex: 1, boundaryGap: false, axisLabel: { show: false } },
          { type: 'category', data: dates, gridIndex: 2, boundaryGap: false, axisLabel: { show: false } },
          { type: 'category', data: dates, gridIndex: 3, boundaryGap: false, axisLabel: { show: false } }
        ],
        yAxis: [
          { type: 'value', scale: true, gridIndex: 0 },
          { type: 'value', scale: true, gridIndex: 1, splitLine: { show: false } },
          { type: 'value', scale: true, gridIndex: 2, splitLine: { show: false } },
          { type: 'value', scale: true, gridIndex: 3, splitLine: { show: false }, min: 0, max: 1 }
        ],
        dataZoom: [
          { type: 'inside', xAxisIndex: [0, 1, 2, 3], start: 70, end: 100 },
          { show: true, xAxisIndex: [0, 1, 2, 3], type: 'slider', bottom: '1%' }
        ],
        series: [
          {
            name: '0050 K線',
            type: 'candlestick',
            data: values,
            itemStyle: { color: '#ef232a', color0: '#14b143', borderColor: '#ef232a', borderColor0: '#14b143' },
            markPoint: {
              symbol: 'arrow',
              symbolSize: 14,
              label: { show: true, color: '#fff', fontSize: 12, formatter: '{c}' },
              data: markPointData
            }
          },
          { name: 'MA60', type: 'line', data: ma60Data, smooth: true, showSymbol: false, lineStyle: { width: 2, color: '#f1c40f' } },
          { name: 'Volume', type: 'bar', xAxisIndex: 1, yAxisIndex: 1, data: volumes, itemStyle: { color: (p) => values[p.dataIndex][1] >= values[p.dataIndex][0] ? '#ef232a' : '#14b143' } },
          { name: 'K', type: 'line', xAxisIndex: 2, yAxisIndex: 2, data: kValues, lineStyle: { width: 1.5, color: '#f39c12' }, showSymbol: false },
          { name: 'D', type: 'line', xAxisIndex: 2, yAxisIndex: 2, data: dValues, lineStyle: { width: 1.5, color: '#3498db' }, showSymbol: false },
          { name: 'AI Score', type: 'line', xAxisIndex: 3, yAxisIndex: 3, data: aiScores, lineStyle: { width: 1.5, color: '#e74c3c' }, areaStyle: { opacity: 0.2, color: '#e74c3c' }, showSymbol: false }
        ]
      };
      
      myChart.setOption(option);
      window.addEventListener('resize', () => myChart.resize());
    }

    if (data.length > 0) {
      // 💡 確保傳給 AI 講評的 K, D 是我們最終萃取出來的正確數值，避免當掉
      const latestData = data[data.length - 1];
      const latestK = kValues[kValues.length - 1];
      const latestD = dValues[dValues.length - 1];
      fetchAiAdvice(latestData, latestK, latestD);
    }

  } catch (err) {
    loading.value = false;
    errorMsg.value = err.message;
  }
});

const fetchAiAdvice = async (latestData, latestK, latestD) => {
  aiLoading.value = true;
  try {
    const aiRequestPayload = { marketData: `今日台股0050收盤價${latestData.close}。K值${latestK}與D值${latestD}。` };
    const response = await fetch('https://fn-quant-ai-2026.azurewebsites.net/api/getaiadvice', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(aiRequestPayload)
    });
    if (!response.ok) throw new Error(`AI API 發生錯誤 (${response.status})`);
    const result = await response.json();
    aiAdvice.value = result.Advice || result.advice || result;
  } catch (err) {
    aiAdvice.value = "⚠️ 無法取得最新 AI 講評，請稍後再試。";
  } finally {
    aiLoading.value = false;
  }
};
</script>

<style scoped>
/* 原本的樣式保持不變 */
.dashboard { padding: 20px; background-color: #121212; color: #ffffff; min-height: 100vh; font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; }
header h1 { font-size: 1.5rem; margin-bottom: 20px; color: #e0e0e0; }
.ai-card { background: linear-gradient(135deg, #1e1e2f, #2a2a40); border-left: 4px solid #00d2ff; padding: 15px 20px; border-radius: 8px; margin-bottom: 20px; box-shadow: 0 4px 6px rgba(0, 0, 0, 0.3); }
.ai-title { color: #00d2ff; font-weight: bold; margin-bottom: 8px; font-size: 1.1rem; }
.ai-content { color: #d1d5db; line-height: 1.6; }
.typing-effect { color: #888; animation: blink 1.5s infinite; }
@keyframes blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }
.chart-box { width: 100%; height: 800px; background-color: #1e1e1e; border-radius: 8px; padding: 10px; }
.loading-container { display: flex; flex-direction: column; align-items: center; justify-content: center; height: 500px; }
.spinner { width: 50px; height: 50px; border: 4px solid rgba(255, 255, 255, 0.1); border-top: 4px solid #00d2ff; border-radius: 50%; animation: spin 1s linear infinite; margin-bottom: 15px; }
@keyframes spin { 0% { transform: rotate(0deg); } 100% { transform: rotate(360deg); } }
.error-msg { color: #ff6b6b; background-color: rgba(255, 107, 107, 0.1); padding: 20px; border-radius: 8px; text-align: center; margin-top: 20px; }
</style>