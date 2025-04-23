namespace SagaTransaction.Abstractions
{
    /// <summary>
    /// Интерфейс этапа обработки саги
    /// </summary>
    public interface ISagaStage
    {
        /// <summary>
        /// Описание этапа
        /// </summary>
        public string StageInfo { get; }
        
        /// <summary>
        /// Статус обработки этапа
        /// </summary>
        public SagaState State { get; init; }
        
        /// <summary>
        /// Метод обработки этапа
        /// </summary>
        /// <param name="transactionId">Id транзакции</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Статус обработки этапа</returns>
        public ValueTask<SagaState> Process(Guid transactionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Метод отката обработки этапа
        /// </summary>
        /// <param name="transactionId">Id транзакции</param>
        /// <param name="cancellationToken">Токен отмены</param>
        /// <returns>Статус отката этапа</returns>
        public ValueTask<SagaState> Rollback(Guid transactionId, CancellationToken cancellationToken = default);
    }
}
