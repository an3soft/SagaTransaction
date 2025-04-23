using Microsoft.Extensions.Logging;
using Moq;
using SagaTransaction;
using SagaTransaction.Abstractions;
using TestSagaTransaction.Stages;

namespace TestSagaTransaction
{
    public class TestSagaOrchestrator
    {
        [Fact]
        public async Task TestCompleted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // этап 1 удачно выполнился без отката
            var stage1 = new Mock<ISagaStage>();
            stage1.SetupGet(s => s.StageInfo).Returns("stage 1");
            stage1.SetupGet(s => s.State).Returns(SagaState.Completed);
            _ = stage1.Setup(s => s.Process(It.Is<Guid>(p => p != Guid.Empty),
                                            It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));

            // этап 2 удачно выполнился без отката
            var stage2 = new Mock<ISagaStage>();
            stage2.SetupGet(s => s.StageInfo).Returns("stage 2");
            stage2.SetupGet(s => s.State).Returns(SagaState.Completed);
            _ = stage2.Setup(s => s.Process(It.Is<Guid>(p => p != Guid.Empty),
                                            It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));

            saga.AddStages([
                stage1.Object,
                stage2.Object,
                ]);

            var state = await saga.Process();

            Assert.Equal(SagaState.Completed, state);
            Assert.Equal(RollbackState.None, saga.Rollbacked);
            Assert.Equal(SagaState.Completed, stage1.Object.State);
            Assert.Equal(SagaState.Completed, stage2.Object.State);
        }

        [Fact]
        public async Task TestFaulted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // этап 1 удачно выполнился и удачно откатился
            var stage1 = new Mock<ISagaStage>();
            stage1.SetupGet(s => s.StageInfo).Returns("stage 1");
            stage1.SetupGet(s => s.State).Returns(SagaState.Completed);
            _ = stage1.Setup(s => s.Process(It.Is<Guid>(p => p != Guid.Empty),
                                        It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));
            _ = stage1.Setup(s => s.Rollback(It.Is<Guid>(p => p != Guid.Empty),
                                         It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));

            // этап 2 неудачно выполнился без отката
            var stage2 = new Mock<ISagaStage>();
            stage2.SetupGet(s => s.StageInfo).Returns("stage 2");
            stage2.SetupGet(s => s.State).Returns(SagaState.Faulted);
            _ = stage2.Setup(s => s.Process(It.Is<Guid>(p => p != Guid.Empty),
                                        It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Faulted));

            saga.AddStages([
                stage1.Object,
                stage2.Object,
                ]);

            var state = await saga.Process();

            Assert.Equal(SagaState.Faulted, state);
            Assert.Equal(RollbackState.Completed, saga.Rollbacked);
            Assert.Equal(SagaState.Completed, stage1.Object.State);
            Assert.Equal(SagaState.Faulted, stage2.Object.State);
        }

        [Fact]
        public async Task TestRealWorldCompleted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // этап 1 удачно выполнился без отката
            ISagaStage stage1 = new TestStage1();
            // этап 2 удачно выполнился без отката
            ISagaStage stage2 = new TestStage1();

            saga.AddStages([
                stage1,
                stage2,
                ]);

            var state = await saga.Process();

            Assert.Equal(SagaState.Completed, state);
            Assert.Equal(RollbackState.None, saga.Rollbacked);
            Assert.Equal(SagaState.Completed, stage1.State);
            Assert.Equal(SagaState.Completed, stage2.State);
        }

        [Fact]
        public async Task TestRealWorldFaulted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // этап 1 удачно выполнился без отката
            ISagaStage stage1 = new TestStage1();
            // этап 2 удачно выполнился без отката
            ISagaStage stage2 = new TestStage2();

            saga.AddStages([
                stage1,
                stage2,
                ]);

            var state = await saga.Process();

            Assert.Equal(SagaState.Faulted, state);
            Assert.Equal(RollbackState.Completed, saga.Rollbacked);
            Assert.Equal(SagaState.Completed, stage1.State);
            Assert.Equal(SagaState.Faulted, stage2.State);
        }
    }
}