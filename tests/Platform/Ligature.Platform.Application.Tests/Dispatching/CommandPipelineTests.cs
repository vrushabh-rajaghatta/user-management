using Ligature.Platform.Application.Dispatching;
using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Tests.Dispatching;

public sealed class CommandPipelineTests
{
    [Fact]
    public async Task ExecuteAsync_should_execute_behaviors_in_order()
    {
        var execution = new List<string>();

        var command = new TestCommand();

        var behavior1 =
            new TestBehavior("Behavior1", execution);

        var behavior2 =
            new TestBehavior("Behavior2", execution);

        var handler =
            new TestHandler(execution);

        var pipeline = new CommandPipeline();

        var result = await pipeline.ExecuteAsync(
            command,
            new ICommandBehavior<TestCommand, string>[]
            {
                behavior1,
                behavior2
            },
            handler,
            CancellationToken.None);

        Assert.Equal("Handled", result);

        Assert.Equal(
            new[]
            {
                "Behavior1-Before",
                "Behavior2-Before",
                "Handler",
                "Behavior2-After",
                "Behavior1-After"
            },
            execution);
    }

    private sealed record TestCommand : ICommand<string>;

    private sealed class TestHandler
        : ICommandHandler<TestCommand, string>
    {
        private readonly List<string> _execution;

        public TestHandler(List<string> execution)
        {
            _execution = execution;
        }

        public Task<string> Handle(
            TestCommand command,
            CancellationToken cancellationToken)
        {
            _execution.Add("Handler");

            return Task.FromResult("Handled");
        }
    }

    private sealed class TestBehavior
        : ICommandBehavior<TestCommand, string>
    {
        private readonly string _name;
        private readonly List<string> _execution;

        public TestBehavior(
            string name,
            List<string> execution)
        {
            _name = name;
            _execution = execution;
        }

        public async Task<string> Handle(
            TestCommand command,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task<string>> next)
        {
            _execution.Add($"{_name}-Before");

            var result = await next(cancellationToken);

            _execution.Add($"{_name}-After");

            return result;
        }
    }
}