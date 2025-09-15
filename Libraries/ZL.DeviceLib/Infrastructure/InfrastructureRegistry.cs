using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ZL.DeviceLib.Storage
{
    public class DbOptions
    {
        public string ConnectionString { get; set; }
        public Dictionary<string, object> Settings { get; set; }
        public string DefaultDbPath { get; set; }
    }

    public interface IInfrastructureRegistrar
    {
        void Register(InfrastructureRegistry registry);
    }

    public class InfrastructureRegistry
    {
        private readonly ConcurrentDictionary<string, Func<DbOptions, IDatabaseService>> _dbFactories =
            new ConcurrentDictionary<string, Func<DbOptions, IDatabaseService>>(StringComparer.OrdinalIgnoreCase);

        public void RegisterDatabase(string type, Func<DbOptions, IDatabaseService> factory)
        {
            if (string.IsNullOrWhiteSpace(type) || factory == null) return;
            _dbFactories[type] = factory;
        }

        public IDatabaseService CreateDatabase(string type, DbOptions options)
        {
            if (string.IsNullOrWhiteSpace(type)) type = "sqlite"; // 默认 sqlite
            if (_dbFactories.TryGetValue(type, out var fac))
                return fac(options);
            // 兼容：若未注册，尝试 sqlite 作为兜底
            if (_dbFactories.TryGetValue("sqlite", out var defFac))
                return defFac(options);
            throw new Exception("未找到数据库提供者: " + type);
        }

        public void LoadPlugins(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
            var dlls = Directory.GetFiles(dir, "*.dll");
            foreach (var f in dlls)
            {
                try
                {
                    var asm = Assembly.LoadFrom(f);
                    var regs = asm.GetTypes().Where(t => !t.IsAbstract && typeof(IInfrastructureRegistrar).IsAssignableFrom(t));
                    foreach (var r in regs)
                    {
                        var inst = (IInfrastructureRegistrar)Activator.CreateInstance(r);
                        inst.Register(this);
                    }
                }
                catch { }
            }
        }
    }
}

