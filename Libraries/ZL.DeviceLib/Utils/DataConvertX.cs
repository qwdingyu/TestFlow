namespace ZL.DeviceLib.Utils
{
    public class DataConvertX
    {

        public static bool ToBool(object o)
        {
            if (o == null) return false;
            if (o is bool b) return b;
            var s = o.ToString().Trim().ToUpperInvariant();
            return s == "1" || s == "TRUE" || s == "ON" || s == "YES";
        }
    }
}
