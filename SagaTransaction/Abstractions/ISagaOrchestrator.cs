namespace SagaTransaction.Abstractions
{
    /// <summary>
    /// Тип статуса обработки
    /// </summary>
    public enum SagaState
    {
        None,
        InProcess,
        Completed,
        Faulted
    };

    /// <summary>
    /// Тип обработки этапов
    /// </summary>
    public enum SagaProcessType
    {
        Sequentional,
        Parallel
    }

    /// <summary>
    /// Интерфейс оркестратора саги
    /// </summary>
    public interface ISagaOrchestrator
    {
        /// <summary>
        /// Id транзакции
        /// </summary>
        public Guid TransactionId { get; }
        
        /// <summary>
        /// Статус обработки
        /// </summary>
        public SagaState State { get; }
        
        /// <summary>
        /// Этапы обработки
        /// </summary>
        public ISagaStage[] Stages { get; }
        
        /// <summary>
        /// Статус отката обработки
        /// </summary>
        public SagaState RollbackState { get; }

        /// <summary>
        /// Добавление этапов обработки
        /// </summary>
        /// <param name="stages">Этапы для обработки</param>
        public void AddStages(IEnumerable<ISagaStage> stages);

        /// <summary>
        /// Выполнение обработки этапов
        /// </summary>
        /// <param name="processType">Тип обработки этапов</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Статус обработки</returns>
        public ValueTask<SagaState> Process(SagaProcessType processType = SagaProcessType.Sequentional, CancellationToken cancellationToken = default);
    }
}
