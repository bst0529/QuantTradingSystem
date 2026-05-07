<template>
  <div class="p-4 bg-gray-900 rounded-lg shadow-xl">
    <h2 class="text-xl font-bold text-green-400 mb-4">0050 量化回測與 AI 情緒決策分析</h2>
    <!-- 圖表容器，高度設定為 600px 容納三層 -->
    <div ref="chartRef" style="width: 100%; height: 600px;"></div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';
import * as echarts from 'echarts';
import axios from 'axios';

const chartRef = ref<HTMLElement | null>(null);

onMounted(async () => {
  if (!chartRef.value) return;
  const myChart = echarts.init(chartRef.value, 'dark'); // 啟用專業深色主題

  try {
    // 1. 呼叫我們在第五階段寫好的 .NET 10 API
    const response = await axios.get(`${import.meta.env.VITE_API_BASE_URL}/strategy/0050?startDate=2023-01-01&endDate=2024-01-01`);
    const data = response.data.results;

    // 2. 解析資料為 ECharts 格式
    const dates = data.map((d: any) => d.date.split('T')[0]);
    const closes = data.map((d: any) => d.close);
    const ma60s = data.map((d: any) => d.ma60);
    const ks = data.map((d: any) => d.k);
    const ds = data.map((d: any) => d.d);
    
    // 3. ECharts 神奇配置：三網格 + 游標連動
    const option = {
      backgroundColor: 'transparent',
      tooltip: {
        trigger: 'axis',
        axisPointer: { type: 'cross' } // 啟用十字游標
      },
      // 核心連動設定：滑鼠在任一圖表滑動，三個圖表的提示框同時觸發
      axisPointer: { link: [{ xAxisIndex: 'all' }] },
      
      // 切割出三個版面配置 (Top: 50%, Middle: 25%, Bottom: 25%)
      grid: [
        { left: '5%', right: '5%', top: '5%', height: '45%' },     // 主圖
        { left: '5%', right: '5%', top: '55%', height: '20%' },    // KD 副圖
        { left: '5%', right: '5%', top: '80%', height: '15%' }     // 訊號圖
      ],
      xAxis: [
        { type: 'category', data: dates, gridIndex: 0, axisLabel: { show: false } },
        { type: 'category', data: dates, gridIndex: 1, axisLabel: { show: false } },
        { type: 'category', data: dates, gridIndex: 2 } // 只有最底層顯示日期
      ],
      yAxis: [
        { type: 'value', gridIndex: 0, scale: true, name: '股價 (TWD)' },
        { type: 'value', gridIndex: 1, min: 0, max: 100, name: 'KD' },
        { type: 'category', gridIndex: 2, data: ['Sell', 'None', 'Buy'], name: '訊號' }
      ],
      // 啟用下方時間軸縮放桿，同時控制三個 X 軸
      dataZoom: [{ type: 'slider', xAxisIndex: [0, 1, 2], bottom: 0 }],
      series: [
        // 第一層：主圖 K線(此處以折線示意)與 MA60
        { name: '收盤價', type: 'line', data: closes, xAxisIndex: 0, yAxisIndex: 0, itemStyle: { color: '#ef5350' } },
        { name: 'MA60', type: 'line', data: ma60s, xAxisIndex: 0, yAxisIndex: 0, itemStyle: { color: '#42a5f5' } },
        
        // 第二層：KD 雙線
        { name: 'K值', type: 'line', data: ks, xAxisIndex: 1, yAxisIndex: 1, itemStyle: { color: '#ffca28' } },
        { name: 'D值', type: 'line', data: ds, xAxisIndex: 1, yAxisIndex: 1, itemStyle: { color: '#ab47bc' } },
        
        // 第三層：交易訊號 (利用散點圖畫出買賣標記)
        { 
          name: 'AI 決策訊號', 
          type: 'scatter', 
          data: data.map((d: any) => d.signal === 1 ? 'Buy' : d.signal === 2 ? 'Sell' : 'None'),
          xAxisIndex: 2, 
          yAxisIndex: 2,
          itemStyle: {
            color: (params: any) => params.value === 'Buy' ? '#66bb6a' : params.value === 'Sell' ? '#ef5350' : 'transparent'
          }
        }
      ]
    };

    myChart.setOption(option);
  } catch (error) {
    console.error('API 請求失敗:', error);
  }
});
</script>