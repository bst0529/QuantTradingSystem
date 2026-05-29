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

// 動態取得今天日期 (YYYY-MM-DD，本地時間)
const getTodayString = () => {
  const today = new Date();
  const year = today.getFullYear();
  const month = String(today.getMonth() + 1).padStart(2, '0');
  const day = String(today.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
};

onMounted(async () => {
  try {
    const startDate = '2023-01-01';
    const endDate = getTodayString();
    
    // 1. 抓取量化交易歷史與指標數據
    // 請確認此網址為你 Azure Container Apps 的主 API 網址
    const response = await fetch(`https://quant-api.purplesmoke-0c574d76.koreacentral.azurecontainerapps.io/api/strategy/0050?startDate=${startDate}&endDate=${endDate}`);
    
    if (!response.ok) {
      throw new Error(`API 請求失敗: ${response.status}`);
    }
    
    const data = await response.json();
    
    if (!data || data.length === 0) {
      throw new Error("查無資料，請確認資料庫是否已寫入數據。");
    }

    // 2. 解析數據供 ECharts 使用
    const dates = [];
    const values = []; // [開, 收, 低, 高] ECharts 預設格式
    const volumes = [];
    const kValues = [];
    const dValues = [];
    const aiScores = [];
    
    data.forEach(item => {
      dates.push(item.date.split('T')[0]); // 只取日期部分
      values.push([item.open, item.close, item.low, item.high]);
      volumes.push(item.volume);
      kValues.push(item.kValue);
      dValues.push(item.dValue);
      aiScores.push(item.sentimentScore || 0.5); // 預設 0.5
    });

    // 3. 計算買賣點 (MarkPoint) 邏輯：KD 黃金/死亡交叉
    const markPointData = [];
    for (let i = 1; i < dates.length; i++) {
      const prevK = kValues[i - 1];
      const prevD = dValues[i - 1];
      const currK = kValues[i];
      const currD = dValues[i];

      // 📈 黃金交叉 (買進)：K 向上突破 D
      if (prevK <= prevD && currK > currD) {
        markPointData.push({
          name: '買進',
          coord: [dates[i], values[i][2]], // 標在當天最低價 (Low) 的位置
          value: 'B',
          itemStyle: { color: '#ef232a' }, // 紅色代表買進
          symbolOffset: [0, 15] // 往下移避免擋住 K 線
        });
      }
      // 📉 死亡交叉 (賣出)：K 向下跌破 D
      else if (prevK >= prevD && currK < currD) {
        markPointData.push({
          name: '賣出',
          coord: [dates[i], values[i][3]], // 標在當天最高價 (High) 的位置
          value: 'S',
          itemStyle: { color: '#14b143' }, // 綠色代表賣出
          symbolRotate: 180, // 箭頭反轉朝下
          symbolOffset: [0, -15] // 往上移
        });
      }
    }

    // 資料處理完畢，關閉 Loading，讓 Vue 把 DOM 顯示出來
    loading.value = false;

    // 4. 等待 DOM 更新後，初始化 ECharts (解決 0x0 白畫面問題)
    await nextTick();
    
    if (chartRef.value) {
      const myChart = echarts.init(chartRef.value, 'dark'); // 使用暗色主題
      
      const option = {
        backgroundColor: '#1e1e1e',
        tooltip: {
          trigger: 'axis',
          axisPointer: { type: 'cross' }
        },
        axisPointer: { link: [{ xAxisIndex: 'all' }] },
        grid: [
          { left: '10%', right: '8%', height: '50%' }, // K 線圖
          { left: '10%', right: '8%', top: '65%', height: '10%' }, // 成交量
          { left: '10%', right: '8%', top: '78%', height: '10%' }, // KD
          { left: '10%', right: '8%', top: '91%', height: '6%' }   // AI 情緒
        ],
        xAxis: [
          { type: 'category', data: dates, gridIndex: 0, boundaryGap: false },
          { type: 'category', data: dates, gridIndex: 1, boundaryGap: false, axisLabel: { show: false } },
          { type: 'category', data: dates, gridIndex: 2, boundaryGap: false, axisLabel: { show: false } },
          { type: 'category', data: dates, gridIndex: 3, boundaryGap: false, axisLabel: { show: false } }
        ],
        yAxis: [
          { scale: true, gridIndex: 0 },
          { scale: true, gridIndex: 1, splitLine: { show: false } },
          { scale: true, gridIndex: 2, splitLine: { show: false } },
          { scale: true, gridIndex: 3, splitLine: { show: false } }
        ],
        dataZoom: [
          { type: 'inside', xAxisIndex: [0, 1, 2, 3], start: 80, end: 100 },
          { show: true, xAxisIndex: [0, 1, 2, 3], type: 'slider', bottom: '1%' }
        ],
        series: [
          {
            name: '0050',
            type: 'candlestick',
            data: values,
            itemStyle: {
              color: '#ef232a',
              color0: '#14b143',
              borderColor: '#ef232a',
              borderColor0: '#14b143'
            },
            // 👇 這裡就是補回來的買賣點標記 👇
            markPoint: {
              symbol: 'arrow',
              symbolSize: 12,
              label: { 
                show: true, 
                color: '#fff', 
                fontSize: 10,
                formatter: '{c}' 
              },
              data: markPointData
            }
          },
          {
            name: 'Volume',
            type: 'bar',
            xAxisIndex: 1,
            yAxisIndex: 1,
            data: volumes,
            itemStyle: {
              color: (params) => {
                return values[params.dataIndex][1] >= values[params.dataIndex][0] ? '#ef232a' : '#14b143';
              }
            }
          },
          {
            name: 'K',
            type: 'line',
            xAxisIndex: 2,
            yAxisIndex: 2,
            data: kValues,
            lineStyle: { width: 1.5, color: '#f39c12' }
          },
          {
            name: 'D',
            type: 'line',
            xAxisIndex: 2,
            yAxisIndex: 2,
            data: dValues,
            lineStyle: { width: 1.5, color: '#3498db' }
          },
          {
            name: 'AI Score',
            type: 'line',
            xAxisIndex: 3,
            yAxisIndex: 3,
            data: aiScores,
            lineStyle: { width: 1, color: '#e74c3c' },
            areaStyle: { opacity: 0.1, color: '#e74c3c' }
          }
        ]
      };
      
      myChart.setOption(option);
      
      // RWD 自適應縮放
      window.addEventListener('resize', () => {
        myChart.resize();
      });
    }

    // 5. 圖表畫完後，背景觸發 AI 講評抓取
    fetchAiAdvice(data[data.length - 1]);

  } catch (err) {
    loading.value = false;
    errorMsg.value = err.message;
  }
});

// 獨立拉出 AI 呼叫函式
const fetchAiAdvice = async (latestData) => {
  aiLoading.value = true;
  try {
    const aiRequestPayload = {
      marketData: `今日台股0050收盤價${latestData.close}，高於季線MA60。K值${latestData.kValue.toFixed(2)}與D值${latestData.dValue.toFixed(2)}。`
    };

    const response = await fetch('https://fn-quant-ai-2026.azurewebsites.net/api/getaiadvice', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(aiRequestPayload)
    });

    if (!response.ok) {
      throw new Error(`AI API 發生錯誤 (${response.status})`);
    }

    const result = await response.json();
    aiAdvice.value = result.Advice || result.advice || result;
  } catch (err) {
    console.error("AI 呼叫失敗:", err);
    aiAdvice.value = "⚠️ 無法取得最新 AI 講評，請稍後再試。";
  } finally {
    aiLoading.value = false;
  }
};
</script>

<style scoped>
.dashboard {
  padding: 20px;
  background-color: #121212;
  color: #ffffff;
  min-height: 100vh;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

header h1 {
  font-size: 1.5rem;
  margin-bottom: 20px;
  color: #e0e0e0;
}

/* AI 卡片樣式 */
.ai-card {
  background: linear-gradient(135deg, #1e1e2f, #2a2a40);
  border-left: 4px solid #00d2ff;
  padding: 15px 20px;
  border-radius: 8px;
  margin-bottom: 20px;
  box-shadow: 0 4px 6px rgba(0, 0, 0, 0.3);
}

.ai-title {
  color: #00d2ff;
  font-weight: bold;
  margin-bottom: 8px;
  font-size: 1.1rem;
}

.ai-content {
  color: #d1d5db;
  line-height: 1.6;
}

.typing-effect {
  color: #888;
  animation: blink 1.5s infinite;
}

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}

/* 圖表容器 */
.chart-box {
  width: 100%;
  height: 800px; /* 為了容納 4 個圖表，高度拉大 */
  background-color: #1e1e1e;
  border-radius: 8px;
  padding: 10px;
}

/* Loading 動畫與錯誤訊息 */
.loading-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 500px;
}

.spinner {
  width: 50px;
  height: 50px;
  border: 4px solid rgba(255, 255, 255, 0.1);
  border-top: 4px solid #00d2ff;
  border-radius: 50%;
  animation: spin 1s linear infinite;
  margin-bottom: 15px;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

.error-msg {
  color: #ff6b6b;
  background-color: rgba(255, 107, 107, 0.1);
  padding: 20px;
  border-radius: 8px;
  text-align: center;
  margin-top: 20px;
}
</style>