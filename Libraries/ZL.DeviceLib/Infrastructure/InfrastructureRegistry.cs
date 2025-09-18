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
        public string Type { get; set; }
        public string ConnectionString { get; set; }
        public Dictionary<string, object> Settings { get; set; }
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
            if (string.IsNullOrWhiteSpace(type))
                return null;
            if (_dbFactories.TryGetValue(type, out var fac))
                return fac(options);
            throw new Exception("未找到数据库提供者: " + type);
        }

    }
}

