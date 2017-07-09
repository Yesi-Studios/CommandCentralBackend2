using FluentNHibernate.Cfg;
using FluentNHibernate.Cfg.Db;
using MySql.Data.MySqlClient;
using NHibernate;
using NHibernate.Caches.SysCache;
using NHibernate.Cfg;
using NHibernate.Metadata;
using NHibernate.Tool.hbm2ddl;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CommandCentral.Framework.Data
{
    public static class DataProvider
    {
        public static ISessionFactory SessionFactory { get; private set; }

        public static SchemaExport Schema { get; private set; }

        public static Configuration Config { get; private set; }

        public static ConcurrentDictionary<Type, IClassMetadata> ClassMetaData { get; private set; }

        public static ISession GetSession()
        {
            return SessionFactory.OpenSession();
        }

        public static void InitializeSessionFactory(MySqlConnectionStringBuilder connectionString)
        {
            Config = Fluently.Configure().Database(MySQLConfiguration.Standard.ConnectionString(connectionString.GetConnectionString(true))
                //.ShowSql()
                )
                .Cache(x => x.UseSecondLevelCache().UseQueryCache()
                .ProviderClass<SysCacheProvider>())
                .Mappings(x => x.FluentMappings.AddFromAssembly(Assembly.GetExecutingAssembly()))
                .BuildConfiguration();

            //We're going to save the schema in case the host wants to use it later.
            Schema = new SchemaExport(Config);

            SessionFactory = Config.BuildSessionFactory();

            ClassMetaData = new ConcurrentDictionary<Type, IClassMetadata>(SessionFactory.GetAllClassMetadata().Select(x => new
            {
                Type = Assembly.GetExecutingAssembly().GetType(x.Key),
                MetaData = x.Value
            })
            .ToDictionary(x => x.Type, x => x.MetaData));
        }
        
    }
}
