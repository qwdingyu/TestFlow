using System;
using System.Globalization;

namespace ZL.DeviceLib.Devices.Mock
{
    public static class MockMessageBuilder
    {
        /// <summary>
        /// 生成噪声计报文：格式 "AWAA, 53.2dBA"
        /// 默认区间 [20, 90]；decimals 控制小数位（默认 1）
        /// </summary>
        public static string BuildAwaaMock(double min = 20.0, double max = 90.0, int decimals = 1, Random rnd = null)
        {
            if (min > max) (min, max) = (max, min);
            if (decimals < 0) decimals = 0;
            rnd ??= new Random();

            // 均匀分布的随机数
            double v = min + rnd.NextDouble() * (max - min);

            // 按指定小数位四舍五入
            double rv = Math.Round(v, decimals, MidpointRounding.AwayFromZero);

            // 使用不随系统文化变化的小数点
            string formatted = rv.ToString("F" + decimals, CultureInfo.InvariantCulture);

            return $"AWAA, {formatted}dBA, ";
        }

        /// <summary>
        /// 生成电阻报文（两路示例）：格式
        /// R1:{value1},-1000.0,{flag1} R2:{value2},-1010.0,{flag2}
        /// 
        /// - 数值默认范围 [80, 480]，避免太小
        /// - decimals 控制小数位，默认 1 位
        /// - 使用 InvariantCulture，保证小数点为 "."
        /// </summary>
        public static string BuildResistanceFrame(double min = 80.0, double max = 480.0, int decimals = 1, Random rnd = null)
        {
            if (min > max) (min, max) = (max, min);
            if (decimals < 0) decimals = 0;
            rnd ??= new Random();

            double v1 = min + rnd.NextDouble() * (max - min);
            double v2 = min + rnd.NextDouble() * (max - min);

            // 四舍五入，固定小数位
            double r1 = Math.Round(v1, decimals, MidpointRounding.AwayFromZero);
            double r2 = Math.Round(v2, decimals, MidpointRounding.AwayFromZero);

            string s1 = r1.ToString("F" + decimals, CultureInfo.InvariantCulture);
            string s2 = r2.ToString("F" + decimals, CultureInfo.InvariantCulture);

            int f1 = rnd.Next(0, 2);
            int f2 = rnd.Next(0, 2);

            // 与现有解析格式保持一致，第二、三字段仍用示例中的占位数
            return $"R1:{s1},-1000.0,{f1} R2:{s2},-1010.0,{f2}";
        }
        /// <summary>
        /// 电阻模拟 双通道
        /// </summary>
        /// <param name="rnd"></param>
        /// <returns></returns>
        public static string BuildResistanceFrame(Random rnd)
        {
            double v1 = rnd.NextDouble() * 1e-2 + 1e-5;
            double v2 = rnd.NextDouble() * 1e-2 + 1e-5;
            string s1 = v1.ToString("0.0000E-03", CultureInfo.InvariantCulture);
            string s2 = v2.ToString("0.0000E-03", CultureInfo.InvariantCulture);
            int f1 = rnd.Next(0, 2), f2 = rnd.Next(0, 2);
            return $"R1:{s1},-1000.0,{f1} R2:{s2},-1010.0,{f2}";
        }

    }
}
