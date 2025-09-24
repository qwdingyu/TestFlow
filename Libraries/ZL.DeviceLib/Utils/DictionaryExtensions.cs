using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZL.DeviceLib.Utils
{
    public static class DictionaryExtensions
    {
        //// 使用扩展方法
        //double value = dict.GetValue("DoubleValue", 0.0);
        public static T GetValue<T>(this Dictionary<string, object> dict, string key, T defVal = default)
        {
            if (dict == null || !dict.ContainsKey(key))
                return defVal;

            try
            {
                object value = dict[key];

                if (value == null || Convert.IsDBNull(value))
                    return defVal;

                if (value is T typedValue)
                    return typedValue;

                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return defVal;
            }
        }
    }

}
