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

            // ���� 1 ������ ���������� ��� ������
            var stage1 = new Mock<ISagaStage>();
            stage1.SetupGet(s => s.StageInfo).Returns("stage 1");
            stage1.SetupGet(s => s.State).Returns(SagaState.Completed);
            _ = stage1.Setup(s => s.Process(It.Is<Guid>(p => p != Guid.Empty),
                                            It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));

            // ���� 2 ������ ���������� ��� ������
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
            Assert.Equal(RollbackState.None, saga.RollbackState);
            Assert.Equal(SagaState.Completed, stage1.Object.State);
            Assert.Equal(SagaState.Completed, stage2.Object.State);
        }

        [Fact]
        public async Task TestFaulted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // ���� 1 ������ ���������� � ������ ���������
            var stage1 = new Mock<ISagaStage>();
            stage1.SetupGet(s => s.StageInfo).Returns("stage 1");
            stage1.SetupGet(s => s.State).Returns(SagaState.Completed);
            _ = stage1.Setup(s => s.Process(It.Is<Guid>(p => p != Guid.Empty),
                                        It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));
            _ = stage1.Setup(s => s.Rollback(It.Is<Guid>(p => p != Guid.Empty),
                                         It.Is<CancellationToken>(t => t == default)))
                      .Returns(ValueTask.FromResult(SagaState.Completed));

            // ���� 2 �������� ���������� ��� ������
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
            Assert.Equal(RollbackState.Completed, saga.RollbackState);
            Assert.Equal(SagaState.Completed, stage1.Object.State);
            Assert.Equal(SagaState.Faulted, stage2.Object.State);
        }

        [Fact]
        public async Task TestRealWorldCompleted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // ���� 1 ������ ���������� ��� ������
            ISagaStage stage1 = new TestStage1();
            // ���� 2 ������ ���������� ��� ������
            ISagaStage stage2 = new TestStage1();

            saga.AddStages([
                stage1,
                stage2,
                ]);

            var state = await saga.Process();

            Assert.Equal(SagaState.Completed, state);
            Assert.Equal(RollbackState.None, saga.RollbackState);
            Assert.Equal(SagaState.Completed, stage1.State);
            Assert.Equal(SagaState.Completed, stage2.State);
        }

        [Fact]
        public async Task TestRealWorldFaulted()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // ���� 1 ������ ���������� ��� ������
            ISagaStage stage1 = new TestStage1();
            // ���� 2 ������ ���������� ��� ������
            ISagaStage stage2 = new TestStage2();

            saga.AddStages([
                stage1,
                stage2,
                ]);

            var state = await saga.Process();

            Assert.Equal(SagaState.Faulted, state);
            Assert.Equal(RollbackState.Completed, saga.RollbackState);
            Assert.Equal(SagaState.Completed, stage1.State);
            Assert.Equal(SagaState.Faulted, stage2.State);
        }

        [Fact]
        public async Task TestRealWorldFaultedInParallel()
        {
            var logger = Mock.Of<ILogger<SagaOrchestrator>>();
            ISagaOrchestrator saga = new SagaOrchestrator(logger);

            // ���� 1 ������ ���������� ��� ������
            ISagaStage stage1 = new TestStage1();
            // ���� 2 ������ ���������� ��� ������
            ISagaStage stage2 = new TestStage2();

            saga.AddStages([
                stage1,
                stage2,
                new TestStage2(103),
                new TestStage2(104),
                new TestStage1(105),
                new TestStage2(106),
                new TestStage2(107),
                new TestStage1(108),
                new TestStage2(109),
                new TestStage1(110),
                ]);

            var state = await saga.Process(SagaProcessType.Parallel);

            Assert.Equal(SagaState.Faulted, state);
            Assert.Equal(RollbackState.Completed, saga.RollbackState);
            Assert.Equal(SagaState.Completed, stage1.State);
            Assert.Equal(SagaState.Faulted, stage2.State);
            Assert.DoesNotContain(saga.Stages, s => s.State == SagaState.InProcess);
            Assert.Contains(saga.Stages, s => s.State == SagaState.None || s.State == SagaState.Completed);
            Assert.Contains(saga.Stages, s => s.State == SagaState.Faulted);
        }
    }
}