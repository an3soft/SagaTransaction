namespace SagaTransaction.Abstractions
{
    public interface ISagaStage
    {
        public string StageInfo { get; }
        public SagaState State { get; init; }
        public ValueTask<SagaState> Process(Guid transactionId, CancellationToken cancellationToken = default);
        public ValueTask<SagaState> Rollback(Guid transactionId, CancellationToken cancellationToken = default);
    }
}
