namespace SagaTransaction.Abstractions
{
    public enum SagaState
    {
        None,
        InProcess,
        Completed,
        Faulted
    };

    public enum RollbackState
    {
        None,
        Completed,
        Faulted
    }

    public enum SagaProcessType
    {
        Sequentional,
        Parallel
    }

    public interface ISagaOrchestrator
    {
        public Guid TransactionId { get; }
        public SagaState State { get; }
        public ISagaStage[] Stages { get; }
        public RollbackState RollbackState { get; }
        public void AddStages(IEnumerable<ISagaStage> stages);
        public ValueTask<SagaState> Process(SagaProcessType processType = SagaProcessType.Sequentional, CancellationToken cancellationToken = default);
    }
}
