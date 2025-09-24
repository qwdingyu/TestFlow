using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace ZL.DeviceLib.Devices.Utils
{
    /// <summary>
    /// 电阻仪通道数据结构
    /// </summary>
    public class ChannelData
    {
        public string ChannelName { get; set; }  // 通道名称 (R1, R2等)
        public double Resistance { get; set; }   // 电阻值
        public double Temperature { get; set; }  // 温度值
        public int Range { get; set; }           // 量程
    }
    public class ResistanceDataParser
    {

        /// <summary>
        /// 解析电阻仪数据
        /// </summary>
        /// <param name="input">输入字符串，格式如: "R1:0.2601E-03,-1000.0,0 R2:11.269E-03,-1000.0,1"</param>
        /// <returns>包含两个通道数据的列表</returns>
        public static List<ChannelData> ParseResistanceData(string input)
        {
            var result = new List<ChannelData>();

            if (string.IsNullOrWhiteSpace(input))
                return result;

            try
            {
                // 按空格分割不同的通道数据
                string[] channelStrings = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string channelString in channelStrings)
                {
                    // 分割通道名称和数据部分
                    int colonIndex = channelString.IndexOf(':');
                    if (colonIndex == -1) continue;

                    string channelName = channelString.Substring(0, colonIndex);
                    string dataPart = channelString.Substring(colonIndex + 1);

                    // 分割数据部分
                    string[] dataValues = dataPart.Split(',');
                    if (dataValues.Length != 3) continue;

                    // 解析各个值
                    var channelData = new ChannelData
                    {
                        ChannelName = channelName,
                        Resistance = ParseScientificNotation(dataValues[0]),
                        Temperature = double.Parse(dataValues[1], CultureInfo.InvariantCulture),
                        Range = int.Parse(dataValues[2])
                    };

                    result.Add(channelData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"解析错误: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 解析科学计数法表示的数值
        /// </summary>
        private static double ParseScientificNotation(string value)
        {
            // 处理科学计数法 (如 0.2601E-03)
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 使用示例
        /// </summary>
        public static void Main(string[] args)
        {
            string input = "R1:0.2601E-03,-1000.0,0 R2:11.269E-03,-1000.0,1";

            List<ChannelData> results = ParseResistanceData(input);

            foreach (var channel in results)
            {
                Console.WriteLine($"通道: {channel.ChannelName}");
                Console.WriteLine($"  电阻值: {channel.Resistance} Ω");
                Console.WriteLine($"  温度值: {channel.Temperature} °C");
                Console.WriteLine($"  量程: {channel.Range}");
                Console.WriteLine();
            }

            // 如果需要单独获取某个通道的值
            var r1Data = results.Find(c => c.ChannelName == "R1");
            var r2Data = results.Find(c => c.ChannelName == "R2");

            if (r1Data != null)
            {
                Console.WriteLine($"R1电阻值: {r1Data.Resistance} Ω");
            }

            if (r2Data != null)
            {
                Console.WriteLine($"R2电阻值: {r2Data.Resistance} Ω");
            }
        }
    }
}
