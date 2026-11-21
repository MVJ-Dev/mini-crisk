using System;

namespace MiniCRisk.Web.Services
{
    /// <summary>Tiny static service locator (the real workload has no DI container either).</summary>
    public static class ServiceRegistry
    {
        private static readonly Lazy<VaultSecretProvider> VaultLazy =
            new Lazy<VaultSecretProvider>(() => new VaultSecretProvider());

        private static readonly Lazy<DocumentStorageManager> StorageLazy =
            new Lazy<DocumentStorageManager>(() => new DocumentStorageManager(Vault));

        private static readonly Lazy<JobQueueManager> QueueLazy =
            new Lazy<JobQueueManager>(() => new JobQueueManager(Vault));

        private static readonly Lazy<CreditRepository> RepositoryLazy =
            new Lazy<CreditRepository>(() => new CreditRepository(Vault));

        private static readonly Lazy<ScoringClient> ScoringLazy =
            new Lazy<ScoringClient>(() => new ScoringClient(Vault));

        private static readonly Lazy<JobMessageProcessor> ProcessorLazy =
            new Lazy<JobMessageProcessor>(() => new JobMessageProcessor(Repository, Scoring));

        private static readonly Lazy<ReferenceDataService> ReferenceLazy =
            new Lazy<ReferenceDataService>(() => new ReferenceDataService(Storage));

        public static VaultSecretProvider Vault { get { return VaultLazy.Value; } }
        public static DocumentStorageManager Storage { get { return StorageLazy.Value; } }
        public static JobQueueManager Queue { get { return QueueLazy.Value; } }
        public static CreditRepository Repository { get { return RepositoryLazy.Value; } }
        public static ScoringClient Scoring { get { return ScoringLazy.Value; } }
        public static JobMessageProcessor Processor { get { return ProcessorLazy.Value; } }
        public static ReferenceDataService Reference { get { return ReferenceLazy.Value; } }
    }
}
