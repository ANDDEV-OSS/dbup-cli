using DbUp.Builder;
using DbUp.Cli.CommandLineOptions;
using DbUp.Cli.DbUpCustomization;
using DbUp.Engine.Output;
using DbUp.Engine.Transactions;
using DbUp.Helpers;
using Optional;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace DbUp.Cli
{
    public static class ConfigurationHelper
    {
        private static bool UseAzureSqlIntegratedSecurity(string connectionString)
        {
            // Use IndexOf to make the code compatible with .NetFramework 4.6
            return !(connectionString.IndexOf("Password", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                                          connectionString.IndexOf("Integrated Security", StringComparison.InvariantCultureIgnoreCase) >= 0 ||
                                          connectionString.IndexOf("Trusted_Connection", StringComparison.InvariantCultureIgnoreCase) >= 0);
        }

        // Each provider's types are reached only through one of these shims.
        // The CLR resolves assembly references when a method is JIT-compiled,
        // so as long as the dispatching switch below mentions no provider type
        // directly, selecting PostgreSQL never loads the SQL Server, MySQL or
        // CockroachDB assemblies. NoInlining is what keeps that true: inlining
        // would fold the body back into the caller and load it again.
        //
        // Signatures deliberately use only dbup-core types.

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UpgradeEngineBuilder SqlServerBuilder(string connectionString, TimeSpan timeout) =>
            DeployChanges.To.SqlDatabase(connectionString).WithExecutionTimeout(timeout);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UpgradeEngineBuilder AzureSqlBuilder(string connectionString, TimeSpan timeout) =>
            DeployChanges.To.SqlDatabase(connectionString, null, UseAzureSqlIntegratedSecurity(connectionString))
                .WithExecutionTimeout(timeout);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UpgradeEngineBuilder PostgresqlBuilder(string connectionString, TimeSpan timeout) =>
            DeployChanges.To.PostgresqlDatabase(connectionString).WithExecutionTimeout(timeout);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UpgradeEngineBuilder MySqlBuilder(string connectionString, TimeSpan timeout) =>
            DeployChanges.To.MySqlDatabase(connectionString).WithExecutionTimeout(timeout);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static UpgradeEngineBuilder CockroachDbBuilder(string connectionString, TimeSpan timeout) =>
            DeployChanges.To.CockroachDbDatabase(connectionString).WithExecutionTimeout(timeout);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureSqlServer(string connectionString, IUpgradeLog logger, int timeoutSec) =>
            EnsureDatabase.For.SqlDatabase(connectionString, logger, timeoutSec);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureAzureSql(string connectionString, IUpgradeLog logger, int timeoutSec) =>
            EnsureDatabase.For.AzureSqlDatabase(connectionString, logger, timeoutSec);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsurePostgresql(string connectionString, IUpgradeLog logger) =>
            EnsureDatabase.For.PostgresqlDatabase(connectionString, logger); // Postgres provider does not support timeout...

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureMySql(string connectionString, IUpgradeLog logger, int timeoutSec) =>
            EnsureDatabase.For.MySqlDatabase(connectionString, logger, timeoutSec);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void EnsureCockroachDb(string connectionString, IUpgradeLog logger) =>
            EnsureDatabase.For.CockroachDbDatabase(connectionString, logger); // Cockroach provider does not support timeout...

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DropSqlServer(string connectionString, IUpgradeLog logger, int timeoutSec) =>
            DropDatabase.For.SqlDatabase(connectionString, logger, timeoutSec);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void DropAzureSql(string connectionString, IUpgradeLog logger) =>
            DropDatabase.For.AzureSqlDatabase(connectionString, logger);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void JournalToSqlServer(UpgradeEngineBuilder builder, string schema, string table) =>
            builder.JournalToSqlTable(schema, table);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void JournalToMySql(UpgradeEngineBuilder builder, string schema, string table) =>
            builder.Configure(c => c.Journal = new MySql.MySqlTableJournal(() => c.ConnectionManager, () => c.Log, schema, table));

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void JournalToPostgresql(UpgradeEngineBuilder builder, string schema, string table) =>
            builder.JournalToPostgresqlTable(schema, table);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void JournalToCockroachDb(UpgradeEngineBuilder builder, string schema, string table) =>
            builder.JournalToCockroachDbTable(schema, table);

        public static Option<UpgradeEngineBuilder, Error> SelectDbProvider(Provider provider, string connectionString, int connectionTimeoutSec)
        {
            var timeout = TimeSpan.FromSeconds(connectionTimeoutSec);

            return provider switch
            {
                Provider.SqlServer => SqlServerBuilder(connectionString, timeout).Some<UpgradeEngineBuilder, Error>(),
                Provider.AzureSql => AzureSqlBuilder(connectionString, timeout).Some<UpgradeEngineBuilder, Error>(),
                Provider.PostgreSQL => PostgresqlBuilder(connectionString, timeout).Some<UpgradeEngineBuilder, Error>(),
                Provider.MySQL => MySqlBuilder(connectionString, timeout).Some<UpgradeEngineBuilder, Error>(),
                Provider.CockroachDB => CockroachDbBuilder(connectionString, timeout).Some<UpgradeEngineBuilder, Error>(),
                _ => Option.None<UpgradeEngineBuilder, Error>(Error.Create(Constants.ConsoleMessages.UnsupportedProvider, provider.ToString())),
            };
        }

        public static Option<bool, Error> EnsureDb(IUpgradeLog logger, Provider provider, string connectionString, int connectionTimeoutSec)
        {
            try
            {
                switch (provider)
                {
                    case Provider.SqlServer:
                        EnsureSqlServer(connectionString, logger, connectionTimeoutSec);
                        return true.Some<bool, Error>();
                    case Provider.AzureSql:
                        if (UseAzureSqlIntegratedSecurity(connectionString))
                        {
                            EnsureAzureSql(connectionString, logger, connectionTimeoutSec);
                        }
                        else
                        {
                            EnsureSqlServer(connectionString, logger, connectionTimeoutSec);
                        }
                        return true.Some<bool, Error>();
                    case Provider.PostgreSQL:
                        EnsurePostgresql(connectionString, logger);
                        return true.Some<bool, Error>();
                    case Provider.MySQL:
                        EnsureMySql(connectionString, logger, connectionTimeoutSec);
                        return true.Some<bool, Error>();
                    case Provider.CockroachDB:
                        EnsureCockroachDb(connectionString, logger);
                        return true.Some<bool, Error>();
                }
            }
            catch (Exception ex)
            {
                return Option.None<bool, Error>(Error.Create("EnsureDb failed: {0}", ex.Message));
            }

            return Option.None<bool, Error>(Error.Create(Constants.ConsoleMessages.UnsupportedProvider, provider.ToString()));
        }

        public static Option<bool, Error> DropDb(IUpgradeLog logger, Provider provider, string connectionString, int connectionTimeoutSec)
        {
            try
            {
                switch (provider)
                {
                    case Provider.SqlServer:
                        DropSqlServer(connectionString, logger, connectionTimeoutSec);
                        return true.Some<bool, Error>();
                    case Provider.AzureSql:
                        if (UseAzureSqlIntegratedSecurity(connectionString))
                        {
                            DropAzureSql(connectionString, logger);
                        }
                        else
                        {
                            DropSqlServer(connectionString, logger, connectionTimeoutSec);
                        }
                        return true.Some<bool, Error>();
                    case Provider.PostgreSQL:
                        return Option.None<bool, Error>(Error.Create("PostgreSQL database provider does not support 'drop' command for now"));
                    case Provider.MySQL:
                        return Option.None<bool, Error>(Error.Create("MySQL database provider does not support 'drop' command for now"));
                    case Provider.CockroachDB:
                        return Option.None<bool, Error>(Error.Create("CockroachDB database provider does not support 'drop' command for now"));
                }
            }
            catch (Exception ex)
            {
                return Option.None<bool, Error>(Error.Create("DropDb failed: {0}", ex.Message));
            }

            return Option.None<bool, Error>(Error.Create(Constants.ConsoleMessages.UnsupportedProvider, provider.ToString()));
        }

        public static Option<UpgradeEngineBuilder, Error> SelectJournal(this Option<UpgradeEngineBuilder, Error> builderOrNone, Provider provider, Journal journal) =>
            builderOrNone.Match(
                some: builder =>
                {
                    if (journal == null)
                    {
                        return builder.JournalTo(new NullJournal()).Some<UpgradeEngineBuilder, Error>();
                    }
                    else if (!journal.IsDefault)
                    {
                        switch (provider)
                        {
                            case Provider.SqlServer:
                                JournalToSqlServer(builder, journal.Schema, journal.Table);
                                break;
                            case Provider.MySQL:
                                JournalToMySql(builder, journal.Schema, journal.Table);
                                break;
                            case Provider.PostgreSQL:
                                JournalToPostgresql(builder, journal.Schema, journal.Table);
                                break;
                            case Provider.CockroachDB:
                                JournalToCockroachDb(builder, journal.Schema, journal.Table);
                                break;
                            default:
                                return Option.None<UpgradeEngineBuilder, Error>(Error.Create($"JournalTo does not support a provider {provider}"));
                        }

                        return builder.Some<UpgradeEngineBuilder, Error>();
                    }
                    else
                    {
                        return builder.Some<UpgradeEngineBuilder, Error>();
                    }
                },
                none: error => Option.None<UpgradeEngineBuilder, Error>(error));

        public static Option<UpgradeEngineBuilder, Error> SelectTransaction(this Option<UpgradeEngineBuilder, Error> builderOrNone, Transaction tran) =>
            builderOrNone.Match(
                some: builder =>
                        tran == Transaction.None
                            ? builder.WithoutTransaction().Some<UpgradeEngineBuilder, Error>()
                            : tran == Transaction.PerScript
                                ? builder.WithTransactionPerScript().Some<UpgradeEngineBuilder, Error>()
                                : tran == Transaction.Single
                                    ? builder.WithTransaction().Some<UpgradeEngineBuilder, Error>()
                                    : Option.None<UpgradeEngineBuilder, Error>(Error.Create(Constants.ConsoleMessages.InvalidTransaction, tran)),
                none: error => Option.None<UpgradeEngineBuilder, Error>(error));

        public static Option<UpgradeEngineBuilder, Error> SelectLogOptions(this Option<UpgradeEngineBuilder, Error> builderOrNone, IUpgradeLog logger, VerbosityLevel verbosity) =>
            builderOrNone
                .Match(
                    some: builder => verbosity != VerbosityLevel.Min
                            ? builder.LogTo(logger).Some<UpgradeEngineBuilder, Error>()
                            : builder.LogToNowhere().Some<UpgradeEngineBuilder, Error>(),
                    none: error => Option.None<UpgradeEngineBuilder, Error>(error))
                .Match(
                    some: builder => verbosity == VerbosityLevel.Detail
                            ? builder.LogScriptOutput().Some<UpgradeEngineBuilder, Error>()
                            : builderOrNone,
                    none: error => Option.None<UpgradeEngineBuilder, Error>(error));

        public static Option<UpgradeEngineBuilder, Error> OverrideConnectionFactory(this Option<UpgradeEngineBuilder, Error> builderOrNone, Option<IConnectionFactory> connectionFactory) =>
            builderOrNone.Match(
                some: builder => connectionFactory.Match(
                    some: factory =>
                    {
                        builder.Configure(c => ((DatabaseConnectionManager)c.ConnectionManager).OverrideFactoryForTest(factory));
                        return builder.Some<UpgradeEngineBuilder, Error>();
                    },
                    none: () => builderOrNone),
                none: error => Option.None<UpgradeEngineBuilder, Error>(error));

        public static Option<UpgradeEngineBuilder, Error> AddVariables(this Option<UpgradeEngineBuilder, Error> builderOrNone, Dictionary<string, string> vars, bool disableVars) =>
            builderOrNone.Match(
                some: builder => (disableVars ? builder.WithVariablesDisabled() : builder.WithVariables(vars)).Some<UpgradeEngineBuilder, Error>(),
                none: error => Option.None<UpgradeEngineBuilder, Error>(error)
            );

        public static Option<bool, Error> LoadEnvironmentVariables(IEnvironment environment, string configFilePath, IEnumerable<string> envFiles)
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (configFilePath == null)
                throw new ArgumentNullException(nameof(configFilePath));

            // .env file  in a current folder
            var defaultEnvFile = Path.Combine(environment.GetCurrentDirectory(), Constants.Default.DotEnvFileName);
            if (environment.FileExists(defaultEnvFile))
            {
                DotNetEnv.Env.Load(defaultEnvFile);
            }
            // .env.local file  in a current folder
            var defaultEnvLocalFile = Path.Combine(environment.GetCurrentDirectory(), Constants.Default.DotEnvLocalFileName);
            if (environment.FileExists(defaultEnvLocalFile))
            {
                DotNetEnv.Env.Load(defaultEnvLocalFile);
            }

            // .env file next to a dbup.yml
            var configFileEnv = Path.Combine(new FileInfo(configFilePath).DirectoryName, Constants.Default.DotEnvFileName);
            if (environment.FileExists(configFileEnv))
            {
                DotNetEnv.Env.Load(configFileEnv);
            }
            // .env.local file next to a dbup.yml
            var configFileEnvLocal = Path.Combine(new FileInfo(configFilePath).DirectoryName, Constants.Default.DotEnvLocalFileName);
            if (environment.FileExists(configFileEnvLocal))
            {
                DotNetEnv.Env.Load(configFileEnvLocal);
            }

            if (envFiles != null)
            {
                foreach (var file in envFiles)
                {
                    Error error = null;
                    ConfigLoader.GetFilePath(environment, file)
                        .Match(
                            some: path => DotNetEnv.Env.Load(path),
                            none: err => error = err);

                    if (error != null)
                    {
                        return Option.None<bool, Error>(error);
                    }
                }
            }

            return true.Some<bool, Error>();
        }
    }
}
